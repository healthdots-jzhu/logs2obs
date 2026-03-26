namespace Logs2Obs.QueryEngine.Tests.AI;

using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Logs2Obs.Core.Graphs;
using Logs2Obs.QueryEngine.AI;
using Logs2Obs.QueryEngine.Options;
using Microsoft.Extensions.Options;

public class GitHubModelsAiServiceTests
{
    [Fact]
    public async Task TranslateToSqlAsync_WhenApiReturnsValidJson_ReturnsParsedResult()
    {
        var response = CreateChatResponse("SELECT 1", "test", "LineChart");
        var service = CreateService(new MockHttpMessageHandler(response));
        var context = new QueryContext { TenantId = "tenant-1" };

        var result = await service.TranslateToSqlAsync("show errors", context, CancellationToken.None);

        result.Sql.Should().Be("SELECT 1");
        result.Explanation.Should().Be("test");
        result.SuggestedGraphType.Should().Be(GraphType.LineChart);
    }

    [Fact]
    public async Task TranslateToSqlAsync_WhenApiReturnsInvalidJson_ThrowsAiQueryException()
    {
        var response = CreateRawChatResponse("not json");
        var service = CreateService(new MockHttpMessageHandler(response));
        var context = new QueryContext { TenantId = "tenant-1" };

        Func<Task> act = () => service.TranslateToSqlAsync("show errors", context, CancellationToken.None);

        await act.Should().ThrowAsync<AiQueryException>();
    }

    [Fact]
    public async Task TranslateToSqlAsync_WhenSqlFailsSafetyCheck_ThrowsSqlSafetyException()
    {
        var response = CreateChatResponse("DROP TABLE users", "bad", "LineChart");
        var service = CreateService(new MockHttpMessageHandler(response));
        var context = new QueryContext { TenantId = "tenant-1" };

        Func<Task> act = () => service.TranslateToSqlAsync("drop users", context, CancellationToken.None);

        await act.Should().ThrowAsync<SqlSafetyException>();
    }

    [Fact]
    public async Task TranslateToSqlAsync_WhenHttpFails_RetriesWithPolly()
    {
        var responseFactory = () => CreateChatResponse("SELECT 1", "retry", "LineChart");
        var handler = new RetryHttpMessageHandler(responseFactory);
        var factory = new TestHttpClientFactory(() => new HttpClient(handler, disposeHandler: false));
        var service = CreateService(factory);
        var context = new QueryContext { TenantId = "tenant-1" };

        var result = await service.TranslateToSqlAsync("show errors", context, CancellationToken.None);

        result.Sql.Should().Be("SELECT 1");
        handler.CallCount.Should().BeGreaterOrEqualTo(3);
        factory.CallCount.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task SuggestGraphsAsync_ReturnsEmptyList()
    {
        var response = CreateChatResponse("SELECT 1", "test", "LineChart");
        var service = CreateService(new MockHttpMessageHandler(response));
        var schema = new QueryResultSchema();

        var result = await service.SuggestGraphsAsync(schema, null, CancellationToken.None);

        result.Should().BeEmpty();
    }

    private static GitHubModelsAiService CreateService(HttpMessageHandler handler) =>
        CreateService(new TestHttpClientFactory(() => new HttpClient(handler, disposeHandler: false)));

    private static GitHubModelsAiService CreateService(IHttpClientFactory httpClientFactory)
    {
        var options = Options.Create(new GitHubModelsOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://example.test",
            Model = "test-model",
            TimeoutSeconds = 5,
            MaxTokens = 100
        });

        var auditLogger = new AiQueryAuditLogger(new NullMetadataStore(), NullLogger<AiQueryAuditLogger>.Instance);
        var safetyValidator = new SqlSafetyValidator();

        return new GitHubModelsAiService(
            httpClientFactory,
            options,
            auditLogger,
            safetyValidator,
            NullLogger<GitHubModelsAiService>.Instance);
    }

    private static HttpResponseMessage CreateChatResponse(string sql, string explanation, string suggestedGraphType)
    {
        var payload = new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(new
                        {
                            sql,
                            explanation,
                            suggestedGraphType
                        })
                    }
                }
            },
            usage = new { prompt_tokens = 5, completion_tokens = 7 }
        };

        var json = JsonSerializer.Serialize(payload);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateRawChatResponse(string content)
    {
        var payload = new
        {
            choices = new[]
            {
                new
                {
                    message = new { content }
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public MockHttpMessageHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response);
    }

    private sealed class RetryHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;
        private int _callCount;

        public RetryHttpMessageHandler(Func<HttpResponseMessage> responseFactory) => _responseFactory = responseFactory;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callCount++;
            if (_callCount <= 2)
                throw new HttpRequestException("Simulated transient failure.");

            return Task.FromResult(_responseFactory());
        }
    }

    private sealed class TestHttpClientFactory(Func<HttpClient> clientFactory) : IHttpClientFactory
    {
        private readonly Func<HttpClient> _clientFactory = clientFactory;

        public int CallCount { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CallCount++;
            return _clientFactory();
        }
    }

    private sealed class NullMetadataStore : IMetadataStore
    {
        public Task<T?> GetAsync<T>(string table, string key, CancellationToken ct = default) =>
            Task.FromResult<T?>(default);

        public Task PutAsync<T>(string table, T entity, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string table, string key, CancellationToken ct = default) =>
            Task.CompletedTask;

        public async IAsyncEnumerable<T> QueryAsync<T>(
            string table,
            Func<T, bool> filter,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
