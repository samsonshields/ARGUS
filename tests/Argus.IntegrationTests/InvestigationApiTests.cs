using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Core.DTOs;
using Argus.Core.Enums;
using Argus.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Argus.IntegrationTests;

public sealed class TestApi(string directory, Dictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configure = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ARGUS_DATA_DIRECTORY"] = directory })
                .AddInMemoryCollection(settings ?? []));
        builder.ConfigureServices(services => configure?.Invoke(services));
    }
}
public sealed class InvestigationApiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "argus-tests-" + Guid.NewGuid());
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const string Route = "/api/v1/investigations";
    [Fact]
    public async Task CrudPersistsAcrossApiRestartAndRequiresDeletionConfirmation()
    {
        Guid id;
        DateTime createdAt;
        using (var factory = new TestApi(directory))
        using (var client = factory.CreateClient())
        {
            var post = await client.PostAsJsonAsync(Route, new InvestigationRequest { Title = " Meridian ", Description = "Fictional training", Region = "Atlantic" }, Json);
            Assert.Equal(HttpStatusCode.Created, post.StatusCode);
            var created = (await post.Content.ReadFromJsonAsync<InvestigationResponse>(Json))!;
            id = created.Id; createdAt = created.CreatedAt;
            Assert.NotEqual(Guid.Empty, id);
            Assert.EndsWith(id.ToString(), post.Headers.Location!.ToString());
            Assert.Equal("Meridian", created.Title);
            Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
            var updated = await client.PutAsJsonAsync($"{Route}/{id}", new InvestigationRequest
            {
                Title = "Meridian revised",
                Status = InvestigationStatus.Archived,
                StartDate = new(2026, 1, 1),
                EndDate = new(2026, 2, 1)
            }, Json);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }
        using (var factory = new TestApi(directory))
        using (var client = factory.CreateClient())
        {
            var item = (await client.GetFromJsonAsync<InvestigationResponse>($"{Route}/{id}", Json))!;
            Assert.Equal("Meridian revised", item.Title);
            Assert.Equal(InvestigationStatus.Archived, item.Status);
            Assert.Equal(createdAt, item.CreatedAt);
            Assert.Equal(new DateOnly(2026, 2, 1), item.EndDate);
            Assert.Equal(DateTimeKind.Utc, item.UpdatedAt.Kind);
            Assert.Single((await client.GetFromJsonAsync<List<InvestigationResponse>>(Route + "?search=MERIDIAN&status=Archived", Json))!);
            Assert.Empty((await client.GetFromJsonAsync<List<InvestigationResponse>>(Route + "?status=Active", Json))!);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"{Route}/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Route}/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Route}/{id}?confirm=true")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Route}/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{Route}/{id}", new InvestigationRequest { Title = "Missing" }, Json)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{Route}/{id}?confirm=true")).StatusCode);
        }
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":\"   \"}")]
    [InlineData("{\"title\":\"Test\",\"startDate\":\"2026-02-01\",\"endDate\":\"2026-01-01\"}")]
    [InlineData("{\"title\":\"Test\",\"status\":\"Invalid\"}")]
    [InlineData("{\"title\":\"Test\",\"status\":42}")]
    [InlineData("{\"title\":\"Test\",\"description\":null}")]
    [InlineData("{broken")]
    public async Task InvalidRequestsReturnProblemDetailsWithoutWrites(string json)
    {
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        var response = await client.PostAsync(Route, new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Empty((await client.GetFromJsonAsync<List<InvestigationResponse>>(Route, Json))!);
    }
    [Theory]
    [InlineData("?status=Unknown")]
    [InlineData("?status=999")]
    public async Task InvalidFiltersAreRejected(string query)
    {
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Route + query)).StatusCode);
    }
    [Fact]
    public async Task MigrationAndDatabaseConstraintsAreApplied()
    {
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
        Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Investigations (Id,Title,Description,Status,CreatedAt,UpdatedAt) VALUES ('bad',' ','','Active','2026-01-01','2026-01-01')"));
        var missing = await client.GetAsync($"{Route}/{Guid.NewGuid()}");
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
