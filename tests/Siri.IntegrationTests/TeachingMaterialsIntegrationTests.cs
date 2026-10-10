using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.Integrations.Storage;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Task P4-03c end to end over real HTTP against the production composition root (<see cref="SiriApiFactory"/>): instructors upload
/// teaching materials (multipart) for an episode or a live session, the bytes go to <see cref="IFileStorage"/> (an in-memory double here,
/// Cloudflare R2 in production), learners with an active enrollment get a short-lived signed link.
/// <list type="bullet">
/// <item>the server — not the client — validates (extension / MIME / size / magic bytes), scans, and chooses the storage key;</item>
/// <item>only the owning instructor (or an admin) writes; only owner / admin / actively-enrolled learners read; everyone else gets a 404 indistinguishable from "no such thing";</item>
/// <item>deleting an attachment, an episode, or a section removes the stored object as well;</item>
/// <item>an unconfigured scanner or an unconfigured bucket answers 503 instead of ever pretending the file was handled.</item>
/// </list>
/// Access tokens are minted directly with the real <see cref="IAccessTokenGenerator"/> (the host's signing key) so the "auth" login limiter is not in the way.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class TeachingMaterialsIntegrationTests : IAsyncLifetime
{
    private static readonly byte[] PdfBytes = [.. "%PDF-1.7\n"u8.ToArray(), .. "hello teaching material"u8.ToArray()];
    private static readonly byte[] ExeBytes = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04];

    private readonly ContainersFixture _containers;

    private readonly InMemoryFileStorage _storage = new();
    private readonly InMemoryFileStorage _limitsStorage = new();

    /// <summary>Fake storage + accept-all scanner, default limits.</summary>
    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Fake storage + accept-all scanner, tiny limits (2 KB files, 2 files per parent).</summary>
    private WebApplicationFactory<Program> _limits = null!;

    /// <summary>Nothing replaced: the real "no scanning engine" scanner (Mode=Required) and the real R2 adapter with no settings.</summary>
    private WebApplicationFactory<Program> _untouched = null!;

    /// <summary>Accept-all scanner but the REAL R2 adapter with no settings.</summary>
    private WebApplicationFactory<Program> _noBucket = null!;

    private HttpClient _client = null!;
    private HttpClient _limitsClient = null!;
    private HttpClient _untouchedClient = null!;
    private HttpClient _noBucketClient = null!;

    public TeachingMaterialsIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var root = Root();
        _factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IFileStorage>(_storage);
            services.AddSingleton<IAttachmentVirusScanner, AcceptAllVirusScanner>();
        }));

        // Migrations are applied by the test, never by the app (database.md: no Database.Migrate() in Program.cs).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }

        _limits = Root(new Dictionary<string, string?>
        {
            ["Catalog:Attachments:MaxFileSizeBytes"] = "2048",
            ["Catalog:Attachments:MaxAttachmentsPerParent"] = "2",
        }).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IFileStorage>(_limitsStorage);
            services.AddSingleton<IAttachmentVirusScanner, AcceptAllVirusScanner>();
        }));

        // Pinned to Required so this host keeps covering the "no scanning engine => refuse" path whatever the shipped appsettings say.
        _untouched = Root(new Dictionary<string, string?> { ["Attachments:VirusScan:Mode"] = "Required" });
        _noBucket = Root().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IAttachmentVirusScanner, AcceptAllVirusScanner>()));

        _client = _factory.CreateClient();
        _limitsClient = _limits.CreateClient();
        _untouchedClient = _untouched.CreateClient();
        _noBucketClient = _noBucket.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _limitsClient.Dispose();
        _untouchedClient.Dispose();
        _noBucketClient.Dispose();
        await _factory.DisposeAsync();
        await _limits.DisposeAsync();
        await _untouched.DisposeAsync();
        await _noBucket.DisposeAsync();

        // WithWebHostBuilder returns a derived factory that does not own its parent.
        foreach (var root in _roots)
        {
            await root.DisposeAsync();
        }
    }

    private readonly List<SiriApiFactory> _roots = [];

    private SiriApiFactory Root(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var root = new SiriApiFactory(_containers, settings);
        _roots.Add(root);
        return root;
    }

    // ---- Arrange helpers ------------------------------------------------------------------------------

    private sealed record Actor(Guid UserId, string Token);

    private sealed record Scene(Actor Instructor, Guid ProfileId, Guid CourseId, Guid SectionId, Guid EpisodeId, Guid SessionId);

    private sealed record Reply(HttpStatusCode Status, string Body)
    {
        public JsonDocument Json => JsonDocument.Parse(string.IsNullOrWhiteSpace(Body) ? "{}" : Body);

        public string? ErrorCode => Json.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;

        /// <summary>ProblemDetails minus the one member that legitimately differs between two otherwise identical errors.</summary>
        public string WithoutTraceId()
        {
            var node = JsonNode.Parse(Body)!.AsObject();
            node.Remove("traceId");
            return node.ToJsonString();
        }
    }

    private async Task<Actor> CreateActorAsync(string roleName)
    {
        var builder = new TestUserBuilder().WithRole(roleName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);
        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, Guid.NewGuid());

        return new Actor(user.Id, token);
    }

    private async Task<(Actor Actor, Guid ProfileId)> CreateInstructorAsync()
    {
        var builder = new TestUserBuilder().WithRole(ROLE.InstructorName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Test Instructor", "Headline", "Bio");
        profile.Approve(clock);
        db.InstructorProfiles().Add(profile);
        await db.SaveChangesAsync();

        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, Guid.NewGuid());

        return (new Actor(user.Id, token), profile.Id);
    }

    /// <summary>A Hybrid course (so it can hold both episodes and live sessions) with one section, one episode and one live session, all created through the real endpoints.</summary>
    private async Task<Scene> CreateSceneAsync()
    {
        var (instructor, profileId) = await CreateInstructorAsync();
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, profileId, DeliveryFormat.Hybrid);

        var section = await SendJsonAsync(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/sections", instructor, new { title = "Section 1" });
        Assert.Equal(HttpStatusCode.Created, section.Status);
        var sectionId = section.Json.RootElement.GetProperty("id").GetGuid();

        var episode = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/catalog/instructor/courses/{courseId}/sections/{sectionId}/episodes",
            instructor,
            new { title = "Episode 1", description = "Desc", isFreePreview = false });
        Assert.Equal(HttpStatusCode.Created, episode.Status);
        var episodeId = episode.Json.RootElement.GetProperty("id").GetGuid();

        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        return new Scene(instructor, profileId, courseId, sectionId, episodeId, sessionId);
    }

    private async Task EnrollAsync(Actor learner, Guid courseId, Action<ENROLLMENT>? shape = null, DateTime? expiresAtUtc = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var enrollment = ENROLLMENT.Create(learner.UserId, courseId, null, EnrollmentSource.Purchase, expiresAtUtc, clock);
        shape?.Invoke(enrollment);
        db.Enrollments().Add(enrollment);
        await db.SaveChangesAsync();
    }

    private async Task<Actor> EnrolledLearnerAsync(Guid courseId)
    {
        var learner = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(learner, courseId);
        return learner;
    }

    private async Task<Reply> SendJsonAsync(HttpMethod method, string uri, Actor? actor, object? json = null, HttpClient? client = null)
    {
        using var request = LiveIntegrationSupport.Authorized(method, uri, actor?.Token);
        if (json is not null)
        {
            request.Content = JsonContent.Create(json);
        }

        return await SendAsync(request, client);
    }

    private async Task<Reply> UploadAsync(string uri, Actor? actor, string fileName, byte[] bytes, string contentType = "application/pdf", HttpClient? client = null)
    {
        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Post, uri, actor?.Token);

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Content = new MultipartFormDataContent { { file, "file", fileName } };

        return await SendAsync(request, client);
    }

    private async Task<Reply> SendAsync(HttpRequestMessage request, HttpClient? client)
    {
        using var response = await (client ?? _client).SendAsync(request);
        return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string EpisodeUri(Scene scene, string suffix = "") => $"/api/catalog/episodes/{scene.EpisodeId}/attachments{suffix}";

    private static string SessionUri(Scene scene, string suffix = "") => $"/api/catalog/live-sessions/{scene.SessionId}/attachments{suffix}";

    private static Guid IdOf(Reply created) => created.Json.RootElement.GetProperty("id").GetGuid();

    private async Task<string> StoredKeyAsync(Guid attachmentId, bool session)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return session
            ? await db.LiveSessionAttachments().AsNoTracking().Where(a => a.Id == attachmentId).Select(a => a.StorageKey).SingleAsync()
            : await db.EpisodeAttachments().AsNoTracking().Where(a => a.Id == attachmentId).Select(a => a.StorageKey).SingleAsync();
    }

    // ---- Episode: upload ------------------------------------------------------------------------------

    [Fact]
    public async Task UploadEpisodeAttachment_OwnerWithPdf_Returns201_StoresTheObjectUnderAServerBuiltKey_AndNeverExposesTheKey()
    {
        var scene = await CreateSceneAsync();

        var reply = await UploadAsync(EpisodeUri(scene), scene.Instructor, "บทที่ 1 slides.pdf", PdfBytes);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        var json = reply.Json.RootElement;
        Assert.Equal("บทที่ 1 slides.pdf", json.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", json.GetProperty("contentType").GetString());
        Assert.Equal(PdfBytes.Length, json.GetProperty("sizeBytes").GetInt64());
        Assert.DoesNotContain("storageKey", reply.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("teaching-materials", reply.Body, StringComparison.OrdinalIgnoreCase);

        var key = await StoredKeyAsync(IdOf(reply), session: false);
        Assert.StartsWith($"teaching-materials/courses/{scene.CourseId:N}/episodes/{scene.EpisodeId:N}/", key);
        Assert.EndsWith(".pdf", key);
        var stored = _storage.Objects[key];
        Assert.Equal(PdfBytes, stored.Content);
        Assert.Equal("application/pdf", stored.ContentType);
        Assert.Equal("บทที่ 1 slides.pdf", stored.DownloadFileName);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_ClientCannotChooseTheStorageKey()
    {
        var scene = await CreateSceneAsync();
        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Post, EpisodeUri(scene), scene.Instructor.Token);
        var file = new ByteArrayContent(PdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        request.Content = new MultipartFormDataContent
        {
            { file, "file", "notes.pdf" },
            { new StringContent("secret/other-courses-object.pdf"), "storageKey" },
            { new StringContent("../../escape.pdf"), "key" },
        };

        var reply = await SendAsync(request, null);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        var key = await StoredKeyAsync(IdOf(reply), session: false);
        Assert.StartsWith("teaching-materials/courses/", key);
        Assert.DoesNotContain("other-courses-object", key);
        Assert.DoesNotContain("escape", key);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_ExecutableDisguisedAsPdf_Returns400_AndNothingIsStored()
    {
        var scene = await CreateSceneAsync();
        var before = _storage.Objects.Count;

        var reply = await UploadAsync(EpisodeUri(scene), scene.Instructor, "innocent.pdf", ExeBytes);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(before, _storage.Objects.Count);
    }

    [Theory]
    [InlineData("tool.exe", "application/octet-stream")]
    [InlineData("page.html", "text/html")]
    [InlineData("pdf-with-wrong-mime.pdf", "image/png")]
    public async Task UploadEpisodeAttachment_DisallowedTypeOrMime_Returns400_AndNothingIsStored(string fileName, string contentType)
    {
        var scene = await CreateSceneAsync();
        var before = _storage.Objects.Count;

        var reply = await UploadAsync(EpisodeUri(scene), scene.Instructor, fileName, PdfBytes, contentType);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(before, _storage.Objects.Count);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_NoFilePart_Returns400()
    {
        var scene = await CreateSceneAsync();
        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Post, EpisodeUri(scene), scene.Instructor.Token);
        request.Content = new MultipartFormDataContent { { new StringContent("nothing here"), "notAFile" } };

        var reply = await SendAsync(request, null);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_AnotherInstructor_Returns403_AndNothingIsStored()
    {
        var scene = await CreateSceneAsync();
        var (stranger, _) = await CreateInstructorAsync();
        var before = _storage.Objects.Count;

        var reply = await UploadAsync(EpisodeUri(scene), stranger, "notes.pdf", PdfBytes);

        Assert.Equal(HttpStatusCode.Forbidden, reply.Status);
        Assert.Equal(before, _storage.Objects.Count);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_EnrolledLearner_Returns403()
    {
        var scene = await CreateSceneAsync();
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        var reply = await UploadAsync(EpisodeUri(scene), learner, "notes.pdf", PdfBytes);

        Assert.Equal(HttpStatusCode.Forbidden, reply.Status);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_Anonymous_Returns401()
    {
        var scene = await CreateSceneAsync();

        var reply = await UploadAsync(EpisodeUri(scene), actor: null, "notes.pdf", PdfBytes);

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_Admin_Returns201()
    {
        var scene = await CreateSceneAsync();
        var admin = await CreateActorAsync(ROLE.AdminName);

        var reply = await UploadAsync(EpisodeUri(scene), admin, "admin.pdf", PdfBytes);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_UnknownEpisode_Returns404()
    {
        var (instructor, _) = await CreateInstructorAsync();

        var reply = await UploadAsync($"/api/catalog/episodes/{Guid.NewGuid()}/attachments", instructor, "notes.pdf", PdfBytes);

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_OverTheConfiguredSize_Returns400()
    {
        var scene = await CreateSceneAsync();
        var big = new byte[3000];
        "%PDF-1.7\n"u8.CopyTo(big);

        var reply = await UploadAsync(EpisodeUri(scene), scene.Instructor, "big.pdf", big, client: _limitsClient);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Empty(_limitsStorage.Objects);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_BeyondTheFileCountCap_Returns409()
    {
        var scene = await CreateSceneAsync();

        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(EpisodeUri(scene), scene.Instructor, "a.pdf", PdfBytes, client: _limitsClient)).Status);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(EpisodeUri(scene), scene.Instructor, "b.pdf", PdfBytes, client: _limitsClient)).Status);
        var third = await UploadAsync(EpisodeUri(scene), scene.Instructor, "c.pdf", PdfBytes, client: _limitsClient);

        Assert.Equal(HttpStatusCode.Conflict, third.Status);
        Assert.Equal(2, _limitsStorage.Objects.Count(o => o.Key.Contains(scene.EpisodeId.ToString("N"))));
    }

    [Fact]
    public async Task UploadEpisodeAttachment_WithNoScanningEngine_Returns503_AndNothingIsStored()
    {
        var scene = await CreateSceneAsync();

        var reply = await UploadAsync(EpisodeUri(scene), scene.Instructor, "notes.pdf", PdfBytes, client: _untouchedClient);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reply.Status);
        Assert.Equal("attachment.virus_scanner_not_configured", reply.ErrorCode);
    }

    [Fact]
    public async Task UploadEpisodeAttachment_WithNoBucketConfigured_Returns503_WithoutLeakingSettings()
    {
        var scene = await CreateSceneAsync();

        var reply = await UploadAsync(EpisodeUri(scene), scene.Instructor, "notes.pdf", PdfBytes, client: _noBucketClient);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reply.Status);
        Assert.Equal(StorageErrors.ProviderNotConfiguredCode, reply.ErrorCode);
        Assert.DoesNotContain("AccessKey", reply.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Storage:R2", reply.Body, StringComparison.OrdinalIgnoreCase);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.EpisodeAttachments().AnyAsync(a => a.EpisodeId == scene.EpisodeId));
    }

    [Fact]
    public async Task UploadEpisodeAttachment_WhenTheBucketIsUnreachable_Returns503_AndLeavesNoRow()
    {
        var scene = await CreateSceneAsync();
        _storage.FailUploads = true;
        try
        {
            var reply = await UploadAsync(EpisodeUri(scene), scene.Instructor, "notes.pdf", PdfBytes);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, reply.Status);
        }
        finally
        {
            _storage.FailUploads = false;
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.EpisodeAttachments().AnyAsync(a => a.EpisodeId == scene.EpisodeId));
    }

    // ---- Episode: list / download -----------------------------------------------------------------

    [Fact]
    public async Task EpisodeAttachments_EnrolledLearner_CanListAndGetAShortLivedSignedLink()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(EpisodeUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        var list = await SendJsonAsync(HttpMethod.Get, EpisodeUri(scene), learner);
        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.Equal("handout.pdf", Assert.Single(list.Json.RootElement.EnumerateArray()).GetProperty("fileName").GetString());
        Assert.DoesNotContain("teaching-materials", list.Body, StringComparison.OrdinalIgnoreCase);

        var before = DateTime.UtcNow;
        var download = await SendJsonAsync(HttpMethod.Get, EpisodeUri(scene, $"/{attachmentId}/download"), learner);

        Assert.Equal(HttpStatusCode.OK, download.Status);
        var json = download.Json.RootElement;
        var key = await StoredKeyAsync(attachmentId, session: false);
        Assert.Contains(key, json.GetProperty("downloadUrl").GetString());
        Assert.StartsWith("https://", json.GetProperty("downloadUrl").GetString());
        var expires = json.GetProperty("expiresAtUtc").GetDateTime().ToUniversalTime();
        Assert.InRange(expires, before.AddSeconds(240), before.AddSeconds(360));
        Assert.DoesNotContain("storageKey", download.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EpisodeAttachments_StrangerAndAnonymous_GetTheSame404AsForAMissingEpisode()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(EpisodeUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var stranger = await CreateActorAsync(ROLE.LearnerName);
        var downloadUri = EpisodeUri(scene, $"/{attachmentId}/download");

        var missing = await SendJsonAsync(HttpMethod.Get, $"/api/catalog/episodes/{Guid.NewGuid()}/attachments", stranger);

        foreach (var caller in new Actor?[] { stranger, null })
        {
            var list = await SendJsonAsync(HttpMethod.Get, EpisodeUri(scene), caller);
            var download = await SendJsonAsync(HttpMethod.Get, downloadUri, caller);

            Assert.Equal(HttpStatusCode.NotFound, list.Status);
            Assert.Equal(HttpStatusCode.NotFound, download.Status);
            Assert.Equal(missing.WithoutTraceId(), list.WithoutTraceId());
        }
    }

    [Fact]
    public async Task EpisodeAttachments_LapsedEnrollment_IsDeniedLikeAStranger()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(EpisodeUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var expired = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(expired, scene.CourseId, e => e.Expire());
        var lapsedByDate = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(lapsedByDate, scene.CourseId, expiresAtUtc: DateTime.UtcNow.AddDays(-1));

        foreach (var caller in new[] { expired, lapsedByDate })
        {
            var download = await SendJsonAsync(HttpMethod.Get, EpisodeUri(scene, $"/{attachmentId}/download"), caller);
            Assert.Equal(HttpStatusCode.NotFound, download.Status);
        }
    }

    [Fact]
    public async Task EpisodeAttachments_OwnerAndAdmin_CanDownloadWithoutAnEnrollment()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(EpisodeUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var admin = await CreateActorAsync(ROLE.AdminName);

        foreach (var caller in new[] { scene.Instructor, admin })
        {
            var download = await SendJsonAsync(HttpMethod.Get, EpisodeUri(scene, $"/{attachmentId}/download"), caller);
            Assert.Equal(HttpStatusCode.OK, download.Status);
        }
    }

    [Fact]
    public async Task DownloadEpisodeAttachment_AttachmentOfAnotherEpisode_Returns404()
    {
        var scene = await CreateSceneAsync();
        var other = await CreateSceneAsync();
        var otherAttachment = IdOf(await UploadAsync(EpisodeUri(other), other.Instructor, "theirs.pdf", PdfBytes));

        // The caller owns `scene` but asks for `other`'s attachment id under its own episode.
        var reply = await SendJsonAsync(HttpMethod.Get, EpisodeUri(scene, $"/{otherAttachment}/download"), scene.Instructor);

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
    }

    // ---- Episode: delete ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteEpisodeAttachment_Owner_Returns204_AndRemovesTheStoredObject()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(EpisodeUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var key = await StoredKeyAsync(attachmentId, session: false);
        Assert.True(_storage.Objects.ContainsKey(key));

        var reply = await SendJsonAsync(HttpMethod.Delete, EpisodeUri(scene, $"/{attachmentId}"), scene.Instructor);

        Assert.Equal(HttpStatusCode.NoContent, reply.Status);
        Assert.False(_storage.Objects.ContainsKey(key));
        var list = await SendJsonAsync(HttpMethod.Get, EpisodeUri(scene), scene.Instructor);
        Assert.Empty(list.Json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task DeleteEpisodeAttachment_AnotherInstructor_Returns403_AndKeepsTheObject()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(EpisodeUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var key = await StoredKeyAsync(attachmentId, session: false);
        var (stranger, _) = await CreateInstructorAsync();

        var reply = await SendJsonAsync(HttpMethod.Delete, EpisodeUri(scene, $"/{attachmentId}"), stranger);

        Assert.Equal(HttpStatusCode.Forbidden, reply.Status);
        Assert.True(_storage.Objects.ContainsKey(key));
    }

    [Fact]
    public async Task DeleteEpisode_RemovesItsAttachmentObjectsFromStorage()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(EpisodeUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var key = await StoredKeyAsync(attachmentId, session: false);

        var reply = await SendJsonAsync(
            HttpMethod.Delete,
            $"/api/catalog/instructor/courses/{scene.CourseId}/sections/{scene.SectionId}/episodes/{scene.EpisodeId}",
            scene.Instructor);

        Assert.Equal(HttpStatusCode.NoContent, reply.Status);
        Assert.False(_storage.Objects.ContainsKey(key));
    }

    [Fact]
    public async Task DeleteSection_RemovesTheAttachmentObjectsOfItsEpisodes()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(EpisodeUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var key = await StoredKeyAsync(attachmentId, session: false);

        var reply = await SendJsonAsync(
            HttpMethod.Delete,
            $"/api/catalog/instructor/courses/{scene.CourseId}/sections/{scene.SectionId}",
            scene.Instructor);

        Assert.Equal(HttpStatusCode.NoContent, reply.Status);
        Assert.False(_storage.Objects.ContainsKey(key));
    }

    // ---- Live session: upload ------------------------------------------------------------------------

    [Fact]
    public async Task UploadSessionAttachment_Owner_Returns201_StoresUnderTheLiveSessionPrefix()
    {
        var scene = await CreateSceneAsync();

        var reply = await UploadAsync(SessionUri(scene), scene.Instructor, "สไลด์คาบสด.pdf", PdfBytes);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        var json = reply.Json.RootElement;
        Assert.Equal(scene.SessionId, json.GetProperty("sessionId").GetGuid());
        Assert.Equal("สไลด์คาบสด.pdf", json.GetProperty("fileName").GetString());
        Assert.DoesNotContain("teaching-materials", reply.Body, StringComparison.OrdinalIgnoreCase);

        var key = await StoredKeyAsync(IdOf(reply), session: true);
        Assert.StartsWith($"teaching-materials/courses/{scene.CourseId:N}/live-sessions/{scene.SessionId:N}/", key);
        Assert.Equal(PdfBytes, _storage.Objects[key].Content);
    }

    [Fact]
    public async Task UploadSessionAttachment_ExecutableDisguisedAsPdf_Returns400()
    {
        var scene = await CreateSceneAsync();
        var before = _storage.Objects.Count;

        var reply = await UploadAsync(SessionUri(scene), scene.Instructor, "innocent.pdf", ExeBytes);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(before, _storage.Objects.Count);
    }

    [Fact]
    public async Task UploadSessionAttachment_AnotherInstructorLearnerAnonymous_AreRefused()
    {
        var scene = await CreateSceneAsync();
        var (stranger, _) = await CreateInstructorAsync();
        var learner = await EnrolledLearnerAsync(scene.CourseId);
        var before = _storage.Objects.Count;

        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(SessionUri(scene), stranger, "a.pdf", PdfBytes)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(SessionUri(scene), learner, "a.pdf", PdfBytes)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UploadAsync(SessionUri(scene), actor: null, "a.pdf", PdfBytes)).Status);
        Assert.Equal(before, _storage.Objects.Count);
    }

    [Fact]
    public async Task UploadSessionAttachment_CancelledSession_Returns409()
    {
        var scene = await CreateSceneAsync();
        var cancel = await SendJsonAsync(
            HttpMethod.Delete,
            $"/api/catalog/instructor/courses/{scene.CourseId}/live-sessions/{scene.SessionId}",
            scene.Instructor);
        Assert.Equal(HttpStatusCode.NoContent, cancel.Status);

        var reply = await UploadAsync(SessionUri(scene), scene.Instructor, "late.pdf", PdfBytes);

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
    }

    [Fact]
    public async Task UploadSessionAttachment_UnknownSession_Returns404()
    {
        var (instructor, _) = await CreateInstructorAsync();

        var reply = await UploadAsync($"/api/catalog/live-sessions/{Guid.NewGuid()}/attachments", instructor, "a.pdf", PdfBytes);

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
    }

    [Fact]
    public async Task UploadSessionAttachment_BeyondTheFileCountCap_Returns409()
    {
        var scene = await CreateSceneAsync();

        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(SessionUri(scene), scene.Instructor, "a.pdf", PdfBytes, client: _limitsClient)).Status);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(SessionUri(scene), scene.Instructor, "b.pdf", PdfBytes, client: _limitsClient)).Status);

        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(SessionUri(scene), scene.Instructor, "c.pdf", PdfBytes, client: _limitsClient)).Status);
    }

    [Fact]
    public async Task UploadSessionAttachment_WithNoBucketConfigured_Returns503()
    {
        var scene = await CreateSceneAsync();

        var reply = await UploadAsync(SessionUri(scene), scene.Instructor, "notes.pdf", PdfBytes, client: _noBucketClient);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reply.Status);
        Assert.Equal(StorageErrors.ProviderNotConfiguredCode, reply.ErrorCode);
    }

    // ---- Live session: list / download / delete -----------------------------------------------------

    [Fact]
    public async Task SessionAttachments_EnrolledLearner_CanListAndGetASignedLink()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(SessionUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        var list = await SendJsonAsync(HttpMethod.Get, SessionUri(scene), learner);
        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.Equal("handout.pdf", Assert.Single(list.Json.RootElement.EnumerateArray()).GetProperty("fileName").GetString());

        var download = await SendJsonAsync(HttpMethod.Get, SessionUri(scene, $"/{attachmentId}/download"), learner);
        Assert.Equal(HttpStatusCode.OK, download.Status);
        var key = await StoredKeyAsync(attachmentId, session: true);
        Assert.Contains(key, download.Json.RootElement.GetProperty("downloadUrl").GetString());
        Assert.DoesNotContain("storageKey", download.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionAttachments_OwnerAndAdmin_CanReadWithoutAnEnrollment()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(SessionUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var admin = await CreateActorAsync(ROLE.AdminName);

        foreach (var caller in new[] { scene.Instructor, admin })
        {
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(HttpMethod.Get, SessionUri(scene), caller)).Status);
            Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(HttpMethod.Get, SessionUri(scene, $"/{attachmentId}/download"), caller)).Status);
        }
    }

    [Fact]
    public async Task SessionAttachments_NotEntitledCallers_GetTheByteIdentical404OfAMissingSession()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(SessionUri(scene), scene.Instructor, "handout.pdf", PdfBytes));

        var stranger = await CreateActorAsync(ROLE.LearnerName);
        var expired = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(expired, scene.CourseId, e => e.Expire());
        var revoked = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(revoked, scene.CourseId, e => e.Revoke());
        var lapsedByDate = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(lapsedByDate, scene.CourseId, expiresAtUtc: DateTime.UtcNow.AddDays(-1));
        var (otherInstructor, _) = await CreateInstructorAsync();

        var missing = await SendJsonAsync(HttpMethod.Get, $"/api/catalog/live-sessions/{Guid.NewGuid()}/attachments", stranger);
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);

        foreach (var caller in new[] { stranger, expired, revoked, lapsedByDate, otherInstructor })
        {
            var list = await SendJsonAsync(HttpMethod.Get, SessionUri(scene), caller);
            var download = await SendJsonAsync(HttpMethod.Get, SessionUri(scene, $"/{attachmentId}/download"), caller);

            Assert.Equal(HttpStatusCode.NotFound, list.Status);
            Assert.Equal(HttpStatusCode.NotFound, download.Status);
            Assert.Equal(missing.WithoutTraceId(), list.WithoutTraceId());
        }
    }

    [Fact]
    public async Task SessionAttachments_Anonymous_Returns401()
    {
        var scene = await CreateSceneAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendJsonAsync(HttpMethod.Get, SessionUri(scene), actor: null)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendJsonAsync(HttpMethod.Get, SessionUri(scene, $"/{Guid.NewGuid()}/download"), actor: null)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendJsonAsync(HttpMethod.Delete, SessionUri(scene, $"/{Guid.NewGuid()}"), actor: null)).Status);
    }

    [Fact]
    public async Task SessionAttachments_OfAnotherCoursesSession_AreNotReachableThroughYourEnrollment()
    {
        var mine = await CreateSceneAsync();
        var theirs = await CreateSceneAsync();
        var theirAttachment = IdOf(await UploadAsync(SessionUri(theirs), theirs.Instructor, "theirs.pdf", PdfBytes));
        var learner = await EnrolledLearnerAsync(mine.CourseId);

        Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Get, SessionUri(theirs), learner)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Get, SessionUri(theirs, $"/{theirAttachment}/download"), learner)).Status);
        // Own session, someone else's attachment id.
        Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Get, SessionUri(mine, $"/{theirAttachment}/download"), learner)).Status);
    }

    [Fact]
    public async Task DeleteSessionAttachment_Owner_Returns204_AndRemovesTheStoredObject()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(SessionUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var key = await StoredKeyAsync(attachmentId, session: true);

        var reply = await SendJsonAsync(HttpMethod.Delete, SessionUri(scene, $"/{attachmentId}"), scene.Instructor);

        Assert.Equal(HttpStatusCode.NoContent, reply.Status);
        Assert.False(_storage.Objects.ContainsKey(key));
        var list = await SendJsonAsync(HttpMethod.Get, SessionUri(scene), scene.Instructor);
        Assert.Empty(list.Json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task DeleteSessionAttachment_AnotherInstructorOrLearner_Returns403_AndKeepsTheObject()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(SessionUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var key = await StoredKeyAsync(attachmentId, session: true);
        var (stranger, _) = await CreateInstructorAsync();
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(HttpMethod.Delete, SessionUri(scene, $"/{attachmentId}"), stranger)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(HttpMethod.Delete, SessionUri(scene, $"/{attachmentId}"), learner)).Status);
        Assert.True(_storage.Objects.ContainsKey(key));
    }

    [Fact]
    public async Task DeleteSessionAttachment_UnknownAttachment_Returns404()
    {
        var scene = await CreateSceneAsync();

        var reply = await SendJsonAsync(HttpMethod.Delete, SessionUri(scene, $"/{Guid.NewGuid()}"), scene.Instructor);

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
    }

    [Fact]
    public async Task SessionAttachments_OfASoftDeletedCourse_AreGone()
    {
        var scene = await CreateSceneAsync();
        var attachmentId = IdOf(await UploadAsync(SessionUri(scene), scene.Instructor, "handout.pdf", PdfBytes));
        var admin = await CreateActorAsync(ROLE.AdminName);
        await LiveIntegrationSupport.RetireCoursesAsync(_factory, [scene.CourseId]);

        Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Get, SessionUri(scene), admin)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Get, SessionUri(scene, $"/{attachmentId}/download"), admin)).Status);
    }
}
