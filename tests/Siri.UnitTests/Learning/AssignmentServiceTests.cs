using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class AssignmentServiceTests
{
    private sealed class FakeAssignmentRepository : IAssignmentRepository
    {
        public readonly Dictionary<Guid, ASSIGNMENT> Assignments = [];

        public Task<ASSIGNMENT?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Assignments.TryGetValue(assignmentId, out var assignment) ? assignment : null);

        public Task<ASSIGNMENT?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(Assignments.Values.FirstOrDefault(a => a.EPISODE_ID == episodeId));

        public void Add(ASSIGNMENT assignment) => Assignments[assignment.ASSIGNMENT_ID] = assignment;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task CreateAsync_CreatesAssignment()
    {
        var repo = new FakeAssignmentRepository();
        var service = new AssignmentService(repo);

        var episodeId = Guid.NewGuid();
        var request = new CreateAssignmentRequest(episodeId, "การบ้าน 1", "ส่งไฟล์ PDF", 7, 20, "pdf,zip");

        var result = await service.CreateAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("การบ้าน 1", result.Value.Title);
        Assert.Equal("ส่งไฟล์ PDF", result.Value.Instructions);
        Assert.Equal(7, result.Value.DueDays);
        Assert.Equal(20, result.Value.MaxFileSizeMb);
        Assert.Equal("pdf,zip", result.Value.AllowedExtensions);
        Assert.Single(repo.Assignments);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesDetails()
    {
        var repo = new FakeAssignmentRepository();
        var service = new AssignmentService(repo);

        var createResult = await service.CreateAsync(Guid.NewGuid(), new CreateAssignmentRequest(Guid.NewGuid(), "การบ้าน", "คำอธิบายเดิม", 5, 10, "pdf"), CancellationToken.None);
        var assignmentId = createResult.Value.Id;

        var updateRequest = new UpdateAssignmentRequest("การบ้าน (แก้ไข)", "คำอธิบายใหม่", 14, 50, "pdf,docx,zip");
        var updateResult = await service.UpdateAsync(Guid.NewGuid(), assignmentId, updateRequest, CancellationToken.None);

        Assert.True(updateResult.IsSuccess);
        Assert.Equal("การบ้าน (แก้ไข)", updateResult.Value.Title);
        Assert.Equal("คำอธิบายใหม่", updateResult.Value.Instructions);
        Assert.Equal(14, updateResult.Value.DueDays);
        Assert.Equal(50, updateResult.Value.MaxFileSizeMb);
    }
}
