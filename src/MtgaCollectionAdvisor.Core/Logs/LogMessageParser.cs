using System.Text;
using System.Text.RegularExpressions;

namespace MtgaCollectionAdvisor.Core.Logs;

public sealed record LogEvent(string MethodName, string Json);

/// <summary>
/// Streaming, line-by-line parser for MTGA's Player.log RPC-style entries:
///
///   [UnityCrossThreadLogger]&lt;date&gt; &lt;time&gt;
///   &lt;== MethodName(guid)
///   {...json payload...}
///
/// or single-line outgoing requests (ignored - "==&gt;"):
///
///   [UnityCrossThreadLogger]==&gt; MethodName {"id":"...","request":"..."}
///
/// Response payloads are written as a single line in practice, but this parser
/// accumulates across lines by tracking brace/bracket depth (ignoring braces
/// inside quoted strings) so it stays correct if a payload ever wraps.
/// </summary>
public sealed partial class LogMessageParser
{
    [GeneratedRegex(@"<==\s+([A-Za-z0-9_.]+)\(")]
    private static partial Regex ResponseHeaderRegex();

    private string? _pendingMethodName;
    private bool _capturing;
    private readonly StringBuilder _buffer = new();
    private int _depth;
    private bool _inString;
    private bool _escapeNext;

    public void Reset()
    {
        _pendingMethodName = null;
        _capturing = false;
        _buffer.Clear();
        _depth = 0;
        _inString = false;
        _escapeNext = false;
    }

    public LogEvent? ProcessLine(string line)
    {
        if (_capturing)
        {
            return TryCompleteCapture(line);
        }

        if (line.Contains("<=="))
        {
            var match = ResponseHeaderRegex().Match(line);
            _pendingMethodName = match.Success ? match.Groups[1].Value : "Unknown";
            return null;
        }

        if (line.Contains("==>"))
        {
            // Outgoing request - never carries collection/inventory data, and may
            // itself contain balanced braces, so just drop any pending capture.
            _pendingMethodName = null;
            return null;
        }

        if (_pendingMethodName is not null)
        {
            _buffer.Clear();
            _depth = 0;
            _inString = false;
            _escapeNext = false;
            _capturing = true;
            return TryCompleteCapture(line);
        }

        return null;
    }

    private LogEvent? TryCompleteCapture(string line)
    {
        var startIndex = 0;
        if (_buffer.Length == 0)
        {
            startIndex = line.IndexOfAny(['{', '[']);
            if (startIndex < 0)
            {
                // No JSON on this line yet; keep waiting without losing the pending method.
                return null;
            }
        }

        for (var i = startIndex; i < line.Length; i++)
        {
            var c = line[i];
            _buffer.Append(c);

            if (_escapeNext)
            {
                _escapeNext = false;
                continue;
            }

            if (c == '\\' && _inString)
            {
                _escapeNext = true;
                continue;
            }

            if (c == '"')
            {
                _inString = !_inString;
                continue;
            }

            if (_inString) continue;

            if (c is '{' or '[')
            {
                _depth++;
            }
            else if (c is '}' or ']')
            {
                _depth--;
                if (_depth == 0)
                {
                    var json = _buffer.ToString();
                    var method = _pendingMethodName!;
                    _pendingMethodName = null;
                    _capturing = false;
                    _buffer.Clear();
                    return new LogEvent(method, json);
                }
            }
        }

        return null;
    }
}
