using System.Text;

namespace RagChatDemo.ChatApi.Chat;

/// <summary>
/// Stateful filter that strips &lt;think&gt;...&lt;/think&gt; blocks (and their contents) from a
/// streamed token sequence. Tags are matched case-insensitively and may be split across calls.
/// An unclosed &lt;think&gt; tag suppresses all subsequent input (reasonable default: the model
/// is still "thinking" if it never emits &lt;/think&gt;).
/// </summary>
public sealed class ThinkTagFilter
{
    private enum State
    {
        Outside,      // Not inside a think block
        Inside,       // Inside a think block (suppressing output)
        MatchOpen,    // Partially matched "<think>" opening tag chars
        MatchClose    // Partially matched "</think>" closing tag chars
    }

    private State _state = State.Outside;
    private int _tagIndex;  // How many chars of the current tag we've matched so far
    private readonly StringBuilder _buffer = new();

    private const string OpenTagText = "<think>";
    private const string CloseTagText = "</think>";

    /// <summary>
    /// Feed one streamed token; returns the substring (possibly empty) that is OUTSIDE any
    /// think block and should be shown to the user.
    /// </summary>
    public string Feed(string token)
    {
        _buffer.Clear();

        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];

            if (_state is State.Outside or State.MatchOpen)
            {
                ProcessOutside(c);
            }
            else if (_state is State.Inside or State.MatchClose)
            {
                ProcessInside(c);
            }
        }

        return _buffer.ToString();
    }

    private void ProcessOutside(char c)
    {
        // If we're in the middle of matching the "<" prefix for an opening tag
        if (_state == State.MatchOpen)
        {
            char expected = OpenTagText[_tagIndex];
            if (char.ToLowerInvariant(c) == expected)
            {
                _tagIndex++;
                if (_tagIndex == OpenTagText.Length)
                {
                    // Full <think> matched — enter Inside state
                    _state = State.Inside;
                    _tagIndex = 0;
                }
                return; // Still matching, don't emit
            }

            // Mismatch: flush the partial match chars as regular text plus this char
            FlushPartialMatch(OpenTagText, c);
            return;
        }

        // Normal outside state — look for '<' which could start a tag
        if (c == '<')
        {
            _state = State.MatchOpen;
            _tagIndex = 1; // We've matched "<"
        }
        else
        {
            _buffer.Append(c);
        }
    }

    private void ProcessInside(char c)
    {
        // If we're in the middle of matching "</" for a closing tag
        if (_state == State.MatchClose)
        {
            char expected = CloseTagText[_tagIndex];
            if (char.ToLowerInvariant(c) == expected)
            {
                _tagIndex++;
                if (_tagIndex == CloseTagText.Length)
                {
                    // Full </think> matched — exit back to Outside
                    _state = State.Outside;
                    _tagIndex = 0;
                }
                return; // Still matching, don't emit
            }

            // Mismatch in closing tag: the "</" and partial chars are just suppressed content
            // inside the think block, so discard them and stay Inside
            _state = State.Inside;
            _tagIndex = 0;
            return;
        }

        // Normal inside state — suppress everything, but look for '<' which could start "</think>"
        if (c == '<')
        {
            _state = State.MatchClose;
            _tagIndex = 1; // We've matched "<"
        }
        // else: just suppress the char
    }

    /// <summary>
    /// When a partial tag match fails, flush all the already-matched chars as regular text,
    /// then also emit the mismatching character.
    /// </summary>
    private void FlushPartialMatch(ReadOnlySpan<char> tag, char nextChar)
    {
        for (int i = 0; i < _tagIndex; i++)
        {
            _buffer.Append(tag[i]);
        }
        _buffer.Append(nextChar);
        _state = State.Outside;
        _tagIndex = 0;
    }
}
