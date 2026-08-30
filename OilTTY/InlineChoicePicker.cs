internal interface IInlineChoicePicker
{
    int Count { get; }

    int SelectedIndex { get; }

    int HighlightedIndex { get; }

    void Move(int delta);

    void MoveToStart();

    void MoveToEnd();

    int VisibleStart(int visibleRows);
}

internal sealed class InlineChoicePicker<T> : IInlineChoicePicker
{
    public InlineChoicePicker(IReadOnlyList<T> items, int selectedIndex)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("A choice picker requires at least one item.", nameof(items));
        }

        Items = items;
        SelectedIndex = Math.Clamp(selectedIndex, 0, items.Count - 1);
        HighlightedIndex = SelectedIndex;
    }

    public IReadOnlyList<T> Items { get; }

    public int Count => Items.Count;

    public int SelectedIndex { get; }

    public int HighlightedIndex { get; private set; }

    public T Highlighted => Items[HighlightedIndex];

    public void Move(int delta) =>
        HighlightedIndex = Math.Clamp(HighlightedIndex + delta, 0, Items.Count - 1);

    public void MoveToStart() => HighlightedIndex = 0;

    public void MoveToEnd() => HighlightedIndex = Items.Count - 1;

    public int VisibleStart(int visibleRows)
    {
        visibleRows = Math.Max(1, visibleRows);
        return Math.Clamp(
            HighlightedIndex - visibleRows + 1,
            0,
            Math.Max(0, Items.Count - visibleRows));
    }
}

internal sealed class InlineMultiChoicePicker<T> : IInlineChoicePicker
{
    private readonly HashSet<int> _selectedIndices;

    public InlineMultiChoicePicker(IReadOnlyList<T> items, IEnumerable<int> selectedIndices)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("A choice picker requires at least one item.", nameof(items));
        }

        Items = items;
        _selectedIndices = selectedIndices
            .Where(index => index >= 0 && index < items.Count)
            .ToHashSet();
        HighlightedIndex = _selectedIndices.Count == 0 ? 0 : _selectedIndices.Min();
    }

    public IReadOnlyList<T> Items { get; }

    public int Count => Items.Count;

    public int SelectedIndex => -1;

    public int HighlightedIndex { get; private set; }

    public T Highlighted => Items[HighlightedIndex];

    public IReadOnlyList<T> Selected => _selectedIndices
        .Order()
        .Select(index => Items[index])
        .ToArray();

    public bool IsSelected(int index) => _selectedIndices.Contains(index);

    public void ToggleHighlighted()
    {
        if (!_selectedIndices.Remove(HighlightedIndex))
        {
            _selectedIndices.Add(HighlightedIndex);
        }
    }

    public void Move(int delta) =>
        HighlightedIndex = Math.Clamp(HighlightedIndex + delta, 0, Items.Count - 1);

    public void MoveToStart() => HighlightedIndex = 0;

    public void MoveToEnd() => HighlightedIndex = Items.Count - 1;

    public int VisibleStart(int visibleRows)
    {
        visibleRows = Math.Max(1, visibleRows);
        return Math.Clamp(
            HighlightedIndex - visibleRows + 1,
            0,
            Math.Max(0, Items.Count - visibleRows));
    }
}
