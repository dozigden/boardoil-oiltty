using Xunit;

public sealed class InlineChoicePickerTests
{
    [Fact]
    public void MovementClampsToTheAvailableChoices()
    {
        var picker = new InlineChoicePicker<string>(["one", "two", "three"], 1);

        picker.Move(-20);
        Assert.Equal(0, picker.HighlightedIndex);
        Assert.Equal("one", picker.Highlighted);

        picker.Move(20);
        Assert.Equal(2, picker.HighlightedIndex);
        Assert.Equal("three", picker.Highlighted);
    }

    [Fact]
    public void VisibleStartKeepsTheHighlightedChoiceInAFullWindow()
    {
        var picker = new InlineChoicePicker<int>(Enumerable.Range(1, 10).ToArray(), 0);

        picker.Move(7);

        Assert.Equal(4, picker.VisibleStart(4));
        Assert.InRange(picker.HighlightedIndex, picker.VisibleStart(4), picker.VisibleStart(4) + 3);
    }
}
