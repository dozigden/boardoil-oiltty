internal static class TestBoardFactory
{
    public static BoardSnapshot Board(params BoardColumn[] columns) =>
        new(1, "Test board", string.Empty, true, "Owner", columns);

    public static BoardColumn Column(int id, params BoardCard[] cards) =>
        new(id, $"Column {id}", id.ToString(), cards);

    public static BoardCard Card(
        int id,
        int columnId,
        string? title = null,
        IReadOnlyList<CardTag>? tags = null,
        int? slickId = null) =>
        new(
            id,
            columnId,
            1,
            "Story",
            "📙",
            title ?? $"Card {id}",
            string.Empty,
            id.ToString(),
            tags ?? [],
            (tags ?? []).Select(tag => tag.Name).ToArray(),
            DateTime.UnixEpoch,
            DateTime.UnixEpoch,
            null,
            null,
            null,
            slickId,
            slickId is null ? null : $"Slick {slickId}",
            null);

    public static CardTag Tag(int id, string name) =>
        new(id, name, "solid", "{\"backgroundColor\":\"#385688\"}", null);
}
