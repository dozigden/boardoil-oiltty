internal sealed class BoardRenderer
{
    public TerminalFrame Render(
        BoardData data,
        BoardLayout layout,
        int? selectedCardId,
        int? movingCardId,
        string status)
    {
        var canvas = new TerminalCanvas(
            layout.Width,
            layout.Height,
            BoardStyles.TextStrong,
            BoardStyles.RootBackground);
        BoardChromeRenderer.DrawFrame(canvas, data, status, movingCardId is not null);

        if (data.Board.Columns.Count == 0)
        {
            canvas.Put(
                3,
                BoardLayoutEngine.ContentStartRow,
                "This board has no columns.",
                BoardStyles.TextMuted);
            return new TerminalFrame(canvas);
        }

        BoardChromeRenderer.DrawColumnHeaders(
            canvas,
            layout.Columns,
            layout.SelectedColumnIndex);
        BoardSlickRenderer.Draw(canvas, data, layout.Cards);
        var movingCard = layout.Cards.FirstOrDefault(card => card.Card.Id == movingCardId);
        foreach (var card in layout.Cards.Where(card => card.Card.Id != movingCardId))
        {
            data.CardTypes.TryGetValue(card.Card.CardTypeId, out var cardType);
            BoardCardRenderer.Draw(
                canvas,
                card,
                BoardStyles.ResolveCard(cardType),
                selected: card.Card.Id == selectedCardId,
                moving: card.Card.Id == movingCardId);
        }

        if (movingCard is not null)
        {
            var raisedCard = movingCard with
            {
                X = movingCard.X - 1,
                Y = movingCard.Y - 1
            };
            BoardCardRenderer.DrawShadow(canvas, raisedCard);
            data.CardTypes.TryGetValue(raisedCard.Card.CardTypeId, out var cardType);
            BoardCardRenderer.Draw(
                canvas,
                raisedCard,
                BoardStyles.ResolveCard(cardType),
                selected: raisedCard.Card.Id == selectedCardId,
                moving: true);
        }

        BoardChromeRenderer.DrawScrollIndicators(canvas, layout.Columns, layout.Cards);
        return new TerminalFrame(canvas);
    }
}
