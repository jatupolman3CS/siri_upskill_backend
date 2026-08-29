using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Features.ResolveContactMessage;

public sealed class ResolveContactMessageHandler(AppDbContext dbContext)
{
    public async Task<Result> HandleAsync(
        Guid messageId,
        Guid adminUserId,
        ResolveContactMessageCommand command,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.ContactMessages()
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            return Result.Failure(DomainError.NotFound($"Contact message '{messageId}' was not found."));
        }

        message.MarkResolved(adminUserId, command.AdminNotes);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
