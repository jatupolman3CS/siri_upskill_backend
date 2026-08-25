using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure;

public sealed class EnrollmentRepository(AppDbContext dbContext) : IEnrollmentRepository
{
    public Task<ENROLLMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Enrollments().FirstOrDefaultAsync(e => e.ENROLLMENT_ID == id, cancellationToken);

    public Task<ENROLLMENT?> GetByUserAndCourseAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
        dbContext.Enrollments().FirstOrDefaultAsync(e => e.USER_ID == userId && e.COURSE_ID == courseId, cancellationToken);

    public IQueryable<ENROLLMENT> Query() => dbContext.Enrollments().AsNoTracking();

    public void Add(ENROLLMENT enrollment) => dbContext.Enrollments().Add(enrollment);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
