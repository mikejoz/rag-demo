using RagChatDemo.ChatApi.Chat;

namespace RagChatDemo.ChatApi.Tests;

public sealed class ThinkTagFilterTests
{
    [Fact]
    public void Feed_NoTags_PassesThroughUnchanged()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("Hello, world! How can I help?");
        Assert.Equal("Hello, world! How can I help?", result);
    }

    [Fact]
    public void Feed_NoTags_MultipleCalls_PassesAllThrough()
    {
        var filter = new ThinkTagFilter();
        var chunks = new[] { "The ", "quick ", "brown ", "fox." };
        var results = string.Join("", chunks.Select(c => filter.Feed(c)));
        Assert.Equal("The quick brown fox.", results);
    }

    [Fact]
    public void Feed_SingleThinkBlock_StripsTagsAndContent()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("Hello <think>this is reasoning</think>world");
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void Feed_ThinkBlockSplitAcrossManySmallCalls_CharByChar_TagName()
    {
        // Feed character-by-character: "<","t","h","i","n","k","r","e","a","s","o","n","<","/","t","h","i","n","k",">","answer"
        var filter = new ThinkTagFilter();
        var tokens = new[]
        {
            "<", "t", "h", "i", "n", "k", ">",  // <think>
            "r", "e", "a", "s", "o", "n",       // reasoning content
            "<", "/", "t", "h", "i", "n", "k", ">", // </think>
            "answer"                             // visible answer
        };

        var results = string.Join("", tokens.Select(t => filter.Feed(t)));
        Assert.Equal("answer", results);
    }

    [Fact]
    public void Feed_TwoThinkBlocksWithTextBeforeBetweenAfter()
    {
        var filter = new ThinkTagFilter();
        var input = "intro <think>thought1</think>middle <think>thought2</think>end";
        var result = filter.Feed(input);
        Assert.Equal("intro middle end", result);
    }

    [Fact]
    public void Feed_TwoThinkBlocksWithTextBeforeBetweenAfter_SplitAcrossChunks()
    {
        var filter = new ThinkTagFilter();
        var chunks = new[]
        {
            "intro ", "<think>thought1</thin", "k>",
            "middle ", "<think>thought2</thi", "nk>",
            "end"
        };

        var results = string.Join("", chunks.Select(c => filter.Feed(c)));
        Assert.Equal("intro middle end", results);
    }

    [Fact]
    public void Feed_UnclosedThinkTag_SuppressesEverythingAfter()
    {
        var filter = new ThinkTagFilter();
        _ = filter.Feed("hello <think>reasoning");
        var result2 = filter.Feed("more reasoning here");
        Assert.Equal("", result2);
    }

    [Fact]
    public void Feed_ThinkImmediatelyFollowedByAnswer()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("<think>reasoning</think>answer right away");
        Assert.Equal("answer right away", result);
    }

    [Fact]
    public void Feed_CaseInsensitive_UppercaseTHINK()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("hello <THINK>reasoning</think>world");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Feed_CaseInsensitive_MixedCaseClose()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("Hello <think>reasoning</Think>answer");
        Assert.Equal("Hello answer", result);
    }

    [Fact]
    public void Feed_CaseInsensitive_AllUppercase()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("before<THINK>secret</THINK>after");
        Assert.Equal("beforeafter", result);
    }

    [Fact]
    public void Feed_NonThinkTag_PassesThrough()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("The <b>bold</b> text");
        Assert.Equal("The <b>bold</b> text", result);
    }

    [Fact]
    public void Feed_LessThanNotPartOfThinkTag_PassesThrough()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("x < y");
        Assert.Equal("x < y", result);
    }

    [Fact]
    public void Feed_TagSplitAcrossTokenBoundary_MiddleOfTagName()
    {
        // Split as "<th" + "ink>" — still detected as a complete tag
        var filter = new ThinkTagFilter();
        var r1 = filter.Feed("<th");
        var r2 = filter.Feed("ink>reasoning</think>visible");
        Assert.Equal("", r1);
        Assert.Equal("visible", r2);
    }

    [Fact]
    public void Feed_EmptyToken_ReturnsEmpty()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Feed_PureAnswer_NoTags_EverythingPassedThrough()
    {
        var filter = new ThinkTagFilter();
        var chunks = new[] { "This ", "is a ", "direct ", "answer." };
        var results = string.Join("", chunks.Select(c => filter.Feed(c)));
        Assert.Equal("This is a direct answer.", results);
    }

    [Fact]
    public void Feed_ThinkAtStart_Suppresses()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("<think>thinking</think>Hello world");
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void Feed_OnlyThinkBlock_ReturnsEmpty()
    {
        var filter = new ThinkTagFilter();
        var result = filter.Feed("<think>just thinking</think>");
        Assert.Equal("", result);
    }
}
