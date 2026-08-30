internal sealed record CardDraft(
    string Title,
    string Description,
    IReadOnlyList<string> TagNames,
    int CardTypeId,
    int BoardColumnId,
    int? AssignedUserId,
    string? SlickName,
    string? ExternalUrl)
{
    public static CardDraft? CreateNew(BoardData data, int boardColumnId)
    {
        var defaultCardType = data.CardTypes.Values.SingleOrDefault(cardType => cardType.IsSystem);
        if (defaultCardType is null)
        {
            return null;
        }

        return new CardDraft(
            string.Empty,
            string.Empty,
            [],
            defaultCardType.Id,
            boardColumnId,
            null,
            null,
            null);
    }

    public static CardDraft From(BoardCard card) =>
        new(
            card.Title,
            card.Description,
            card.TagNames.ToArray(),
            card.CardTypeId,
            card.BoardColumnId,
            card.AssignedUserId,
            card.SlickName,
            card.ExternalUrl);
}
