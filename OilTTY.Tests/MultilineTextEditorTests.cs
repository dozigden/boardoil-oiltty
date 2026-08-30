using Xunit;

public sealed class MultilineTextEditorTests
{
    [Fact]
    public void Backspace_RemovesACompleteEmojiGrapheme()
    {
        var editor = new MultilineTextEditor("A👩‍💻B");
        editor.HandleKey(Key(ConsoleKey.End), 20);
        editor.HandleKey(Key(ConsoleKey.LeftArrow), 20);

        editor.HandleKey(Key(ConsoleKey.Backspace), 20);

        Assert.Equal("AB", editor.Text);
    }

    [Fact]
    public void EnterAndDelete_EditAcrossLogicalLines()
    {
        var editor = new MultilineTextEditor("firstsecond");
        for (var count = 0; count < 5; count++)
        {
            editor.HandleKey(Key(ConsoleKey.RightArrow), 20);
        }

        editor.HandleKey(Key(ConsoleKey.Enter, '\r'), 20);
        editor.HandleKey(Key(ConsoleKey.Delete), 20);

        Assert.Equal("first\necond", editor.Text);
    }

    [Fact]
    public void VerticalMovement_PreservesDisplayColumnAcrossWideGraphemes()
    {
        var editor = new MultilineTextEditor("ab🙂c\n12345");
        editor.HandleKey(Key(ConsoleKey.End), 20);

        editor.HandleKey(Key(ConsoleKey.DownArrow), 20);

        Assert.Equal(1, editor.LineIndex);
        Assert.Equal(5, editor.CharacterIndex);
    }

    [Fact]
    public void VisualLayout_WrapsAndKeepsCursorOnAVisibleCell()
    {
        var editor = new MultilineTextEditor("123456789");
        for (var count = 0; count < 9; count++)
        {
            editor.HandleKey(Key(ConsoleKey.RightArrow), 4);
        }

        var layout = editor.CreateVisualLayout(4);

        Assert.Equal(["1234", "5678", "9"], layout.Lines.Select(line => line.Text));
        Assert.Equal(2, layout.CursorRow);
        Assert.Equal(1, layout.CursorColumn);
    }

    [Fact]
    public void SingleLineEditor_StartsAtEndAndHonoursMaximumLength()
    {
        var editor = new MultilineTextEditor(
            "abc",
            allowNewLines: false,
            maximumLength: 4,
            cursorAtEnd: true);

        editor.HandleKey(Key(ConsoleKey.X, 'x'), 20);
        editor.HandleKey(Key(ConsoleKey.Y, 'y'), 20);
        var enterHandled = editor.HandleKey(Key(ConsoleKey.Enter, '\r'), 20);

        Assert.Equal("abcx", editor.Text);
        Assert.False(enterHandled);
        Assert.Equal(4, editor.CharacterIndex);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char character = '\0') =>
        new(character, key, shift: false, alt: false, control: false);
}
