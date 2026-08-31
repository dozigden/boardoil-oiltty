using System.Net;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Xunit;

public sealed class BoardOilClientCardTests
{
    private const string CardJson =
        """
        {
          "id": 42,
          "boardColumnId": 3,
          "cardTypeId": 28,
          "cardTypeName": "OilTTY",
          "cardTypeEmoji": "⌨️",
          "title": "Open and edit cards",
          "description": "Preserve every field.",
          "sortKey": "A1",
          "tags": [
            {
              "id": 4,
              "name": "UI",
              "styleName": "solid",
              "stylePropertiesJson": "{\"backgroundColor\":\"#385688\"}",
              "emoji": "✨"
            },
            {
              "id": 9,
              "name": "Tech Debt",
              "styleName": "solid",
              "stylePropertiesJson": "{}",
              "emoji": "💰️"
            }
          ],
          "tagNames": ["UI", "Tech Debt"],
          "cardCreatedUtc": "2026-08-20T10:15:00Z",
          "cardUpdatedUtc": "2026-08-28T17:00:00Z",
          "assignedUserId": 7,
          "assignedUserDisplayName": "Luke",
          "assignedUserImageRelativePath": "/images/luke.png",
          "slickId": 12,
          "slickName": "Editor",
          "externalUrl": "https://example.test/story/42"
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BoardCard_DeserializesCompleteBoardOilContract()
    {
        var card = DeserializeCard();

        Assert.Equal(42, card.Id);
        Assert.Equal(["UI", "Tech Debt"], card.TagNames);
        Assert.Equal(new DateTime(2026, 8, 20, 10, 15, 0, DateTimeKind.Utc), card.CardCreatedUtc);
        Assert.Equal(new DateTime(2026, 8, 28, 17, 0, 0, DateTimeKind.Utc), card.CardUpdatedUtc);
        Assert.Equal(7, card.AssignedUserId);
        Assert.Equal("Luke", card.AssignedUserDisplayName);
        Assert.Equal("/images/luke.png", card.AssignedUserImageRelativePath);
        Assert.Equal("https://example.test/story/42", card.ExternalUrl);
    }

    [Fact]
    public async Task LoadBoardAsync_MapsAllEditorLookupsForEachSelectedBoard()
    {
        var requestedPaths = new ConcurrentBag<string>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requestedPaths.Add(path);
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var boardId = int.Parse(segments[2]);
            var dataJson = segments.Length == 3
                ? $$"""
                    {
                      "id": {{boardId}},
                      "name": "Board {{boardId}}",
                      "description": "",
                      "slickCohesionModeEnabled": true,
                      "currentUserRole": "Contributor",
                      "columns": []
                    }
                    """
                : segments[3] switch
                {
                    "card-types" => $$"""
                        [{
                          "id": {{boardId * 100 + 1}},
                          "name": "Type {{boardId}}",
                          "emoji": "⌨️",
                          "styleName": "auto",
                          "stylePropertiesJson": "{}",
                          "isSystem": true
                        }]
                        """,
                    "slicks" => $$"""
                        [{
                          "id": {{boardId * 100 + 2}},
                          "name": "Slick {{boardId}}",
                          "styleName": "solid",
                          "stylePropertiesJson": "{\"backgroundColor\":\"#385688\"}"
                        }]
                        """,
                    "tags" => $$"""
                        [{
                          "id": {{boardId * 100 + 3}},
                          "name": "Tag {{boardId}}",
                          "styleName": "solid",
                          "stylePropertiesJson": "{}",
                          "emoji": "✨"
                        }]
                        """,
                    "members" => $$"""
                        [{
                          "userId": {{boardId * 100 + 4}},
                          "userName": "user{{boardId}}",
                          "displayName": "Member {{boardId}}",
                          "profileImageRelativePath": "/member-{{boardId}}.png",
                          "role": "Contributor",
                          "createdAtUtc": "2026-08-20T10:15:00Z",
                          "updatedAtUtc": "2026-08-28T17:00:00Z"
                        }]
                        """,
                    _ => throw new InvalidOperationException($"Unexpected lookup path {path}.")
                };
            return Task.FromResult(SuccessResponse(dataJson));
        });
        await using var client = CreateClient(handler);

        var first = await client.LoadBoardAsync(1, TestContext.Current.CancellationToken);
        var second = await client.LoadBoardAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal("Board 1", first.Board.Name);
        Assert.Equal("Type 1", Assert.Single(first.CardTypes).Value.Name);
        Assert.True(Assert.Single(first.CardTypes).Value.IsSystem);
        Assert.Equal("Slick 1", Assert.Single(first.Slicks).Value.Name);
        Assert.Equal("Tag 1", Assert.Single(first.Tags).Name);
        Assert.Equal("Member 1", Assert.Single(first.Members).DisplayName);
        Assert.Equal("Board 2", second.Board.Name);
        Assert.Equal("Type 2", Assert.Single(second.CardTypes).Value.Name);
        Assert.Equal("Slick 2", Assert.Single(second.Slicks).Value.Name);
        Assert.Equal("Tag 2", Assert.Single(second.Tags).Name);
        Assert.Equal("Member 2", Assert.Single(second.Members).DisplayName);
        Assert.Equal(
            new[] { 1, 2 }
                .SelectMany(boardId => new[]
                {
                    $"/api/boards/{boardId}",
                    $"/api/boards/{boardId}/card-types",
                    $"/api/boards/{boardId}/members",
                    $"/api/boards/{boardId}/slicks",
                    $"/api/boards/{boardId}/tags"
                })
                .Order(),
            requestedPaths.Order());
        Assert.DoesNotContain(requestedPaths, path => path.Contains("comments", StringComparison.Ordinal));
    }

    [Fact]
    public void From_PreservesEveryFullStateUpdateField()
    {
        var card = DeserializeCard();

        var draft = CardDraft.From(card);

        Assert.Equal(card.Title, draft.Title);
        Assert.Equal(card.Description, draft.Description);
        Assert.Equal(card.TagNames, draft.TagNames);
        Assert.NotSame(card.TagNames, draft.TagNames);
        Assert.Equal(card.CardTypeId, draft.CardTypeId);
        Assert.Equal(card.BoardColumnId, draft.BoardColumnId);
        Assert.Equal(card.AssignedUserId, draft.AssignedUserId);
        Assert.Equal(card.SlickName, draft.SlickName);
        Assert.Equal(card.ExternalUrl, draft.ExternalUrl);
    }

    [Fact]
    public async Task UpdateCardAsync_SendsExactFullStatePutAndMapsUpdatedCard()
    {
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/api/boards/1/cards/42", request.RequestUri?.AbsolutePath);
            Assert.Equal("api-token", request.Headers.Authorization?.Parameter);

            using var body = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;
            Assert.Equal(
                [
                    "assignedUserId",
                    "boardColumnId",
                    "cardTypeId",
                    "description",
                    "externalUrl",
                    "slickName",
                    "tagNames",
                    "title"
                ],
                root.EnumerateObject().Select(property => property.Name).Order().ToArray());
            Assert.Equal("Edited title", root.GetProperty("title").GetString());
            Assert.Equal("Preserve every field.", root.GetProperty("description").GetString());
            Assert.Equal(["UI", "Tech Debt"], root.GetProperty("tagNames").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(28, root.GetProperty("cardTypeId").GetInt32());
            Assert.Equal(3, root.GetProperty("boardColumnId").GetInt32());
            Assert.Equal(7, root.GetProperty("assignedUserId").GetInt32());
            Assert.Equal("Editor", root.GetProperty("slickName").GetString());
            Assert.Equal("https://example.test/story/42", root.GetProperty("externalUrl").GetString());

            return JsonResponse(
                HttpStatusCode.OK,
                "{\"success\":true,\"data\":" + CardJson.Replace("Open and edit cards", "Edited title")
                + ",\"statusCode\":200,\"message\":null}");
        });
        await using var client = CreateClient(handler);
        var draft = CardDraft.From(DeserializeCard()) with { Title = "Edited title" };

        var updated = await client.UpdateCardAsync(
            1,
            42,
            draft,
            TestContext.Current.CancellationToken);

        Assert.Equal("Edited title", updated.Title);
        Assert.Equal(["UI", "Tech Debt"], updated.TagNames);
        Assert.Equal("https://example.test/story/42", updated.ExternalUrl);
    }

    [Fact]
    public async Task CreateCardAsync_SendsExactFullDraftPostAndMapsCreatedCard()
    {
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/boards/1/cards", request.RequestUri?.AbsolutePath);
            Assert.Equal("api-token", request.Headers.Authorization?.Parameter);

            using var body = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;
            Assert.Equal(
                [
                    "assignedUserId",
                    "boardColumnId",
                    "cardTypeId",
                    "description",
                    "externalUrl",
                    "slickName",
                    "tagNames",
                    "title"
                ],
                root.EnumerateObject().Select(property => property.Name).Order().ToArray());
            Assert.Equal("Created card", root.GetProperty("title").GetString());
            Assert.Equal("New description", root.GetProperty("description").GetString());
            Assert.Equal(["UI"], root.GetProperty("tagNames").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(28, root.GetProperty("cardTypeId").GetInt32());
            Assert.Equal(3, root.GetProperty("boardColumnId").GetInt32());
            Assert.Equal(7, root.GetProperty("assignedUserId").GetInt32());
            Assert.Equal("Editor", root.GetProperty("slickName").GetString());
            Assert.Equal("https://example.test/new", root.GetProperty("externalUrl").GetString());

            var createdJson = CardJson
                .Replace("Open and edit cards", "Created card")
                .Replace("Preserve every field.", "New description")
                .Replace("[\"UI\", \"Tech Debt\"]", "[\"UI\"]")
                .Replace("https://example.test/story/42", "https://example.test/new");
            return JsonResponse(
                HttpStatusCode.Created,
                "{\"success\":true,\"data\":" + createdJson
                + ",\"statusCode\":201,\"message\":null}");
        });
        await using var client = CreateClient(handler);
        var draft = new CardDraft(
            "Created card",
            "New description",
            ["UI"],
            28,
            3,
            7,
            "Editor",
            "https://example.test/new");

        var created = await client.CreateCardAsync(
            1,
            draft,
            TestContext.Current.CancellationToken);

        Assert.Equal("Created card", created.Title);
        Assert.Equal("New description", created.Description);
        Assert.Equal(["UI"], created.TagNames);
        Assert.Equal("https://example.test/new", created.ExternalUrl);
    }

    [Fact]
    public async Task MoveCardAsync_SendsMovePatchAndMapsAuthoritativeCard()
    {
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/api/boards/1/cards/42/move", request.RequestUri?.AbsolutePath);
            Assert.Equal("api-token", request.Headers.Authorization?.Parameter);

            using var body = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;
            Assert.Equal(
                ["boardColumnId", "positionAfterCardId"],
                root.EnumerateObject().Select(property => property.Name).Order().ToArray());
            Assert.Equal(7, root.GetProperty("boardColumnId").GetInt32());
            Assert.Equal(41, root.GetProperty("positionAfterCardId").GetInt32());

            var movedJson = CardJson
                .Replace("\"boardColumnId\": 3", "\"boardColumnId\": 7")
                .Replace("\"sortKey\": \"A1\"", "\"sortKey\": \"A2\"");
            return JsonResponse(
                HttpStatusCode.OK,
                "{\"success\":true,\"data\":" + movedJson
                + ",\"statusCode\":200,\"message\":null}");
        });
        await using var client = CreateClient(handler);

        var moved = await client.MoveCardAsync(
            1,
            new CardMove(42, 7, 41),
            TestContext.Current.CancellationToken);

        Assert.Equal(7, moved.BoardColumnId);
        Assert.Equal("A2", moved.SortKey);
    }

    [Fact]
    public async Task LoadCardCommentsAsync_UsesCardScopedEndpointAndMapsAuthors()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/boards/1/cards/42/comments", request.RequestUri?.AbsolutePath);
            Assert.Equal("api-token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(SuccessResponse(
                """
                [{
                  "id": 9,
                  "cardId": 42,
                  "authorUserId": 7,
                  "text": "First line\nSecond line",
                  "postedAtUtc": "2026-08-31T08:20:00Z",
                  "authorDisplayName": "Luke",
                  "authorImageRelativePath": "/images/luke.png"
                }]
                """));
        });
        await using var client = CreateClient(handler);

        var comments = await client.LoadCardCommentsAsync(
            1,
            42,
            TestContext.Current.CancellationToken);

        var comment = Assert.Single(comments);
        Assert.Equal(9, comment.Id);
        Assert.Equal(42, comment.CardId);
        Assert.Equal(7, comment.AuthorUserId);
        Assert.Equal("First line\nSecond line", comment.Text);
        Assert.Equal("Luke", comment.AuthorDisplayName);
        Assert.Equal("/images/luke.png", comment.AuthorImageRelativePath);
    }

    [Fact]
    public async Task CreateCardCommentAsync_SendsTextOnlyAndMapsCreatedComment()
    {
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/boards/1/cards/42/comments", request.RequestUri?.AbsolutePath);
            Assert.Equal("api-token", request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(["text"], body.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Equal("A new comment", body.RootElement.GetProperty("text").GetString());
            return JsonResponse(
                HttpStatusCode.Created,
                """
                {
                  "success": true,
                  "data": {
                    "id": 10,
                    "cardId": 42,
                    "authorUserId": 7,
                    "text": "A new comment",
                    "postedAtUtc": "2026-08-31T08:25:00Z",
                    "authorDisplayName": "Luke",
                    "authorImageRelativePath": null
                  },
                  "statusCode": 201,
                  "message": null
                }
                """);
        });
        await using var client = CreateClient(handler);

        var comment = await client.CreateCardCommentAsync(
            1,
            42,
            "A new comment",
            TestContext.Current.CancellationToken);

        Assert.Equal(10, comment.Id);
        Assert.Equal("A new comment", comment.Text);
    }

    [Fact]
    public async Task CreateCardCommentAsync_ExposesTextValidationErrors()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.UnprocessableEntity,
            """
            {
              "success": false,
              "data": null,
              "statusCode": 422,
              "message": "Validation failed.",
              "validationErrors": { "text": ["Comment text is required."] }
            }
            """)));
        await using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BoardOilRequestException>(
            () => client.CreateCardCommentAsync(
                1,
                42,
                string.Empty,
                TestContext.Current.CancellationToken));

        Assert.Equal(["Comment text is required."], exception.ValidationErrors["text"]);
    }

    [Fact]
    public async Task CreateCardAsync_ExposesBoardOilValidationErrors()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.UnprocessableEntity,
            """
            {
              "success": false,
              "data": null,
              "statusCode": 422,
              "message": "Validation failed.",
              "validationErrors": {
                "title": ["A title is required."]
              }
            }
            """)));
        await using var client = CreateClient(handler);
        var draft = new CardDraft(string.Empty, string.Empty, [], 28, 3, null, null, null);

        var exception = await Assert.ThrowsAsync<BoardOilRequestException>(
            () => client.CreateCardAsync(
                1,
                draft,
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Equal(["A title is required."], exception.ValidationErrors["title"]);
    }

    [Fact]
    public async Task UpdateCardAsync_ExposesBoardOilValidationErrors()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.UnprocessableEntity,
            """
            {
              "success": false,
              "data": null,
              "statusCode": 422,
              "message": "Validation failed.",
              "validationErrors": {
                "title": ["A title is required."],
                "tagNames": ["Unknown tag."]
              }
            }
            """)));
        await using var client = CreateClient(handler);
        var draft = CardDraft.From(DeserializeCard()) with { Title = string.Empty };

        var exception = await Assert.ThrowsAsync<BoardOilRequestException>(
            () => client.UpdateCardAsync(
                1,
                42,
                draft,
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Equal(["A title is required."], exception.ValidationErrors["title"]);
        Assert.Equal(["Unknown tag."], exception.ValidationErrors["tagNames"]);
    }

    private static BoardCard DeserializeCard() =>
        JsonSerializer.Deserialize<BoardCard>(CardJson, JsonOptions)!;

    private static BoardOilClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://boardoil.test/") };
        var transport = new AuthenticatedBoardOilTransport(httpClient, sessionStore: null);
        transport.UseApiToken("api-token");
        return new BoardOilClient(transport);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage SuccessResponse(string dataJson) =>
        JsonResponse(
            HttpStatusCode.OK,
            "{\"success\":true,\"data\":" + dataJson + ",\"statusCode\":200,\"message\":null}");

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
