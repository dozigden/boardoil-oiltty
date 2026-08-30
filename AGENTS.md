# OilTTY

OilTTY ("Oil Tee") is a standalone C# terminal client for BoardOil.

## Additional guidance

Read the relevant guidance before working in that area:

- [AGENTS/SourceControl.md](AGENTS/SourceControl.md) - Read before using source control.

`README` files are for human user information, not agent execution guidance.

## Current intent

- Visual quality is the immediate priority.
- This begins as an exploratory project; avoid production architecture until justified.
- Treat BoardOil and other external projects as read-only unless explicitly asked to change them.

## Visual requirements

- Use truecolour terminal rendering.
- Handle graphemes and double-width emoji correctly.
- Preserve BoardOil card-type and tag colours.
- Honour BoardOil card-type border modes: omit the normal border for `borderMode: none`, while retaining the selected-card border.
- Tags always have names; emoji may accompany the name.
- Align the leftmost emoji across a card's title, assignee, and tag rows. Tag caps begin one cell earlier so their label emoji shares the title and assignee emoji column.
- Keep card height content-driven: wrap titles, omit empty tag and assignee rows, and wrap overflowing tags as indivisible units rather than splitting a tag across lines.
- Render the visible top of the next card when it extends below a column viewport; clip its card and slick layers above the footer.
- Preserve each column's vertical viewport while navigating; entering a column selects its nearest visible card rather than its previous selection.
- Render each slick as an independent coloured layer.
- Slicks must merge around cards vertically and bridge across columns.
- Use a global board canvas with two vertical subcells per terminal row.
- Show overflow with a muted-lavender right-half scrollbar thumb on the column divider, rendered above slicks.
- Do not draw an outer window border; give the reclaimed edge cells and rows to board content.
- Centre a terminal-native 3×3 emoji interpretation of the BoardOil mosaic above a bold `BoardOil` wordmark and the board picker list, retaining the mosaic's empty bottom-middle cell.
- Kitty profile images may be explored later.

## Technology

- Use C# and .NET.
- Keep the terminal renderer under our control; libraries may assist with input and terminal lifecycle.
- Maintain the HTTP API as the boundary between OilTTY and BoardOil.
