using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.Modules.Payout.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// <c>PUT /api/payout/instructor/payout-account</c> (X-33) end to end over real HTTP against the production composition root (<see cref="SiriApiFactory"/>):
/// JWT bearer + the InstructorOnly policy, the per-user write rate limit, the validation filter, <c>InstructorPayoutAccountService</c> and the real database
/// (encryption at rest, the unique key per instructor, the verification state the payout batch reads). Requires PostgreSQL + Redis like every test in this collection
/// (Docker, or the local external-services mode of <see cref="ExternalTestServices"/>).
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class InstructorPayoutAccountEndpointTests : IAsyncLifetime
{
    private const string AccountPath = "/api/payout/instructor/payout-account";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public InstructorPayoutAccountEndpointTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            // Migrations are applied here, not by the app (database.md: never Database.Migrate() in Program.cs).
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(TestUserBuilder Builder, USER User)> CreateUserAsync(string? role)
    {
        var builder = new TestUserBuilder();
        if (role is not null)
        {
            builder.WithRole(role);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);

        return (builder, user);
    }

    /// <summary>An instructor the way the platform makes one: Instructor role plus an approved profile (the id the money tables are keyed by).</summary>
    private async Task<(string Token, Guid UserId, Guid ProfileId)> CreateInstructorAsync()
    {
        var (builder, user) = await CreateUserAsync(ROLE.InstructorName);

        Guid profileId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Payout Account Instructor", "Headline", "Bio");
            profile.Approve(clock);
            db.InstructorProfiles().Add(profile);
            await db.SaveChangesAsync();
            profileId = profile.Id;
        }

        return (await LoginAsync(builder), user.Id, profileId);
    }

    private async Task<string> LoginAsync(TestUserBuilder builder)
    {
        using var response = await _client.PostAsJsonAsync("/api/identity/login", new
        {
            email = builder.Email,
            password = builder.Password,
            deviceId = $"payout-account-{Guid.NewGuid():N}",
            deviceName = "Payout Account Test Device",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);

        return body.AccessToken;
    }

    private async Task<string> CreateAdminTokenAsync()
    {
        var (builder, _) = await CreateUserAsync(ROLE.AdminName);
        return await LoginAsync(builder);
    }

    private static object Body(
        string bankCode = "KBANK",
        string accountNo = "0123456789",
        string accountName = "สมชาย สบายดี",
        string? taxId = "1234567890123",
        string taxPayerType = "Individual") =>
        new { bankCode, accountNo, accountName, taxId, taxPayerType };

    private Task<HttpResponseMessage> PutAsync(string? token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, AccountPath) { Content = JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetMineAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AccountPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> AdminVerifyAsync(string adminToken, Guid profileId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payout/admin/payout-accounts/{profileId}/verify");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private async Task<INSTRUCTOR_PAYOUT_ACCOUNT?> StoredAccountAsync(Guid profileId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.InstructorPayoutAccounts().AsNoTracking().SingleOrDefaultAsync(a => a.INSTRUCTOR_ID == profileId);
    }

    /// <summary>What payout-batch creation and the transfer-file export see: only verified accounts.</summary>
    private async Task<bool> BatchWouldPayAsync(Guid profileId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInstructorPayoutAccountRepository>();
        var verified = await repository.GetVerifiedAccountsAsync([profileId], CancellationToken.None);
        return verified.ContainsKey(profileId);
    }

    // ---- Access ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_WithoutBearerToken_Returns401()
    {
        using var response = await PutAsync(null, Body());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_SignedInLearner_IsRejectedByTheInstructorOnlyPolicy_With403()
    {
        var (builder, _) = await CreateUserAsync(ROLE.LearnerName);
        var token = await LoginAsync(builder);

        using var response = await PutAsync(token, Body());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_InstructorRoleButNoInstructorProfile_Returns403AndStoresNothing()
    {
        var (builder, _) = await CreateUserAsync(ROLE.InstructorName);
        var token = await LoginAsync(builder);
        var rowsBefore = await CountAccountsAsync();

        using var response = await PutAsync(token, Body());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // The database is shared by the whole test collection, so compare before/after rather than expecting an empty table.
        Assert.Equal(rowsBefore, await CountAccountsAsync());
    }

    private async Task<int> CountAccountsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.InstructorPayoutAccounts().CountAsync();
    }

    // ---- Create / update ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_FirstSave_Returns201WithTheMaskedAccount_StoresItEncrypted_AndLeavesItUnverified()
    {
        var (token, _, profileId) = await CreateInstructorAsync();

        using var response = await PutAsync(token, Body());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("0123456789", raw);
        Assert.DoesNotContain("1234567890123", raw);

        using var document = JsonDocument.Parse(raw);
        var json = document.RootElement;
        Assert.Equal(
            ["accountName", "bankCode", "id", "instructorId", "maskedAccountNo", "taxId", "taxPayerType", "verifiedAtUtc"],
            json.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(profileId, json.GetProperty("instructorId").GetGuid());
        Assert.Equal("KBANK", json.GetProperty("bankCode").GetString());
        Assert.Equal("***-***-6789", json.GetProperty("maskedAccountNo").GetString());
        Assert.Equal("***-***-0123", json.GetProperty("taxId").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("verifiedAtUtc").ValueKind);

        var stored = await StoredAccountAsync(profileId);
        Assert.NotNull(stored);
        Assert.DoesNotContain("0123456789", stored.ACCOUNT_NO_ENCRYPTED);
        Assert.DoesNotContain("1234567890123", stored.TAX_ID);
        Assert.Null(stored.VERIFIED_AT_UTC);
        Assert.False(await BatchWouldPayAsync(profileId));

        // GET returns the very same account.
        using var mine = await GetMineAsync(token);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal(json.GetProperty("id").GetGuid(), (await ReadJsonAsync(mine)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Put_ResendingTheSameDetailsAfterAdminVerification_Returns200AndKeepsItVerified()
    {
        var (token, _, profileId) = await CreateInstructorAsync();
        var adminToken = await CreateAdminTokenAsync();
        using (await PutAsync(token, Body()))
        {
        }

        using var verify = await AdminVerifyAsync(adminToken, profileId);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.True(await BatchWouldPayAsync(profileId));
        var verifiedAt = (await StoredAccountAsync(profileId))!.VERIFIED_AT_UTC;
        Assert.NotNull(verifiedAt);

        using var again = await PutAsync(token, Body());

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.NotEqual(JsonValueKind.Null, (await ReadJsonAsync(again)).GetProperty("verifiedAtUtc").ValueKind);
        Assert.Equal(verifiedAt, (await StoredAccountAsync(profileId))!.VERIFIED_AT_UTC);
        Assert.True(await BatchWouldPayAsync(profileId));
    }

    [Theory]
    [InlineData("bank code")]
    [InlineData("account number")]
    [InlineData("account holder name")]
    [InlineData("tax id")]
    [InlineData("tax payer type")]
    public async Task Put_ChangingAVerifiedAccount_Returns200_ResetsTheVerification_AndTheBatchSkipsItUntilAnAdminVerifiesAgain(string changed)
    {
        var (token, _, profileId) = await CreateInstructorAsync();
        var adminToken = await CreateAdminTokenAsync();
        using (await PutAsync(token, Body()))
        {
        }

        using (await AdminVerifyAsync(adminToken, profileId))
        {
        }

        Assert.True(await BatchWouldPayAsync(profileId));

        object newDetails = changed switch
        {
            "bank code" => Body(bankCode: "SCB"),
            "account number" => Body(accountNo: "9998887776"),
            "account holder name" => Body(accountName: "สมหญิง ใจดี"),
            "tax id" => Body(taxId: "9999999999999"),
            "tax payer type" => Body(taxPayerType: "Corporate"),
            _ => throw new ArgumentOutOfRangeException(nameof(changed), changed, null),
        };

        using var response = await PutAsync(token, newDetails);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadJsonAsync(response)).GetProperty("verifiedAtUtc").ValueKind);
        Assert.Null((await StoredAccountAsync(profileId))!.VERIFIED_AT_UTC);
        Assert.False(await BatchWouldPayAsync(profileId));

        using var reverify = await AdminVerifyAsync(adminToken, profileId);
        Assert.Equal(HttpStatusCode.OK, reverify.StatusCode);
        Assert.True(await BatchWouldPayAsync(profileId));
    }

    [Fact]
    public async Task Put_ANewAccountNumber_IsStoredEncryptedAndTheOldOneIsGone()
    {
        var (token, _, profileId) = await CreateInstructorAsync();
        using (await PutAsync(token, Body()))
        {
        }

        using var response = await PutAsync(token, Body(accountNo: "9998887776"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("***-***-7776", (await ReadJsonAsync(response)).GetProperty("maskedAccountNo").GetString());
        var stored = await StoredAccountAsync(profileId);
        Assert.NotNull(stored);
        Assert.DoesNotContain("9998887776", stored.ACCOUNT_NO_ENCRYPTED);
        await using var scope = _factory.Services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<ISensitiveDataProtector>();
        Assert.Equal("9998887776", protector.Decrypt(stored.ACCOUNT_NO_ENCRYPTED));
    }

    [Fact]
    public async Task Put_InvalidBody_Returns400AndChangesNothing()
    {
        var (token, _, profileId) = await CreateInstructorAsync();
        using (await PutAsync(token, Body()))
        {
        }

        using var response = await PutAsync(token, Body(bankCode: "", accountNo: "9998887776"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await StoredAccountAsync(profileId);
        Assert.Equal("KBANK", stored!.BANK_CODE);
    }

    // ---- Ownership -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_AnInstructorIdInTheBodyIsIgnored_TheCallerCanOnlyEverSaveTheirOwnAccount()
    {
        var (tokenA, _, profileA) = await CreateInstructorAsync();
        var (tokenB, _, profileB) = await CreateInstructorAsync();
        using (await PutAsync(tokenB, Body(accountNo: "2222222222", accountName: "Instructor B")))
        {
        }

        // A tries to name B's profile (and B's user) in the body: the fields are not part of the contract and change nothing.
        using var response = await PutAsync(tokenA, new
        {
            bankCode = "SCB",
            accountNo = "1111111111",
            accountName = "Instructor A",
            taxId = (string?)null,
            taxPayerType = "Individual",
            instructorId = profileB,
            userId = profileB,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(profileA, (await ReadJsonAsync(response)).GetProperty("instructorId").GetGuid());
        var storedB = await StoredAccountAsync(profileB);
        Assert.Equal("KBANK", storedB!.BANK_CODE);
        Assert.Equal("Instructor B", storedB.ACCOUNT_NAME);
        Assert.Equal("Instructor A", (await StoredAccountAsync(profileA))!.ACCOUNT_NAME);

        using var bMine = await GetMineAsync(tokenB);
        Assert.Equal("Instructor B", (await ReadJsonAsync(bMine)).GetProperty("accountName").GetString());
    }

    // ---- Double submit ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_TwoSimultaneousFirstSaves_BothSucceed_OneCreatesTheOtherUpdates_AndOnlyOneRowExists()
    {
        var (token, _, profileId) = await CreateInstructorAsync();

        var responses = await Task.WhenAll(PutAsync(token, Body()), PutAsync(token, Body()));
        try
        {
            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Created],
                responses.Select(r => r.StatusCode).Order().ToArray());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.InstructorPayoutAccounts().CountAsync(a => a.INSTRUCTOR_ID == profileId));
    }

    [Fact]
    public async Task Repository_AfterADuplicateInsertLosesTheRace_DiscardLetsTheSameContextReadAndUpdateTheWinnersRow()
    {
        // The mechanics the service's race handling relies on, against the real database: a second row for the same instructor is rejected by the unique
        // key, and after Discard the same (scoped) DbContext can load the winner's row and save an update to it.
        var (token, _, profileId) = await CreateInstructorAsync();
        using (await PutAsync(token, Body()))
        {
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInstructorPayoutAccountRepository>();
        var protector = scope.ServiceProvider.GetRequiredService<ISensitiveDataProtector>();

        var loser = INSTRUCTOR_PAYOUT_ACCOUNT.Create(profileId, "SCB", protector.Encrypt("9998887776"), "Race Loser", null);
        repository.Add(loser);
        await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChangesAsync(CancellationToken.None));
        repository.Discard(loser);

        var winner = await repository.GetByInstructorIdAsync(profileId, CancellationToken.None);
        Assert.NotNull(winner);
        Assert.NotEqual(loser.INSTRUCTOR_PAYOUT_ACCOUNT_ID, winner.INSTRUCTOR_PAYOUT_ACCOUNT_ID);

        var outcome = winner.UpdateDetails("SCB", protector.Encrypt("9998887776"), true, winner.ACCOUNT_NAME, null, true, winner.TAX_PAYER_TYPE);
        await repository.SaveChangesAsync(CancellationToken.None);

        Assert.True(outcome.Changed);
        var stored = await StoredAccountAsync(profileId);
        Assert.Equal("SCB", stored!.BANK_CODE);
        Assert.Equal(winner.INSTRUCTOR_PAYOUT_ACCOUNT_ID, stored.INSTRUCTOR_PAYOUT_ACCOUNT_ID);
        Assert.Null(stored.TAX_ID);
    }

    // ---- Rate limit ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_RateLimitIsPerUser_The11thSaveInTenMinutesIsA429Problem_AndAnotherInstructorIsUnaffected()
    {
        var (burstToken, _, _) = await CreateInstructorAsync();
        var (otherToken, _, _) = await CreateInstructorAsync();

        for (var i = 1; i <= 10; i++)
        {
            using var allowed = await PutAsync(burstToken, Body());
            Assert.True(allowed.IsSuccessStatusCode, $"request {i} should be allowed but was {(int)allowed.StatusCode}");
        }

        using var rejected = await PutAsync(burstToken, Body());
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", rejected.Headers.CacheControl?.ToString() ?? string.Empty);
        Assert.Equal("rate_limited", (await ReadJsonAsync(rejected)).GetProperty("errorCode").GetString());

        using var other = await PutAsync(otherToken, Body());
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    // ---- POST is untouched -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Post_StillCreatesOnce_AndStillAnswers409OnceTheAccountExists()
    {
        var (token, _, _) = await CreateInstructorAsync();

        using var first = new HttpRequestMessage(HttpMethod.Post, AccountPath) { Content = JsonContent.Create(Body()) };
        first.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var created = await _client.SendAsync(first);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var second = new HttpRequestMessage(HttpMethod.Post, AccountPath) { Content = JsonContent.Create(Body()) };
        second.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var conflict = await _client.SendAsync(second);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        // ...while PUT now updates what POST created.
        using var put = await PutAsync(token, Body(bankCode: "SCB"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
    }
}
