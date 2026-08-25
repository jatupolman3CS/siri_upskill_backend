using Siri.Modules.Cms.Domain;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Request payload for POST /api/cms/admin/posts/{id}/status. POST, not PUT/PATCH — an action against a
/// single resource's lifecycle state, same "action verb, not a field replace" shape
/// <c>Siri.Modules.Catalog.Features.ApproveCourse</c>/<c>RejectCourse</c> use for <c>Course.Status</c>.
/// <para>
/// A single generic status-change command, not a dedicated <c>Publish</c>/<c>Archive</c> pair — unlike
/// <c>Course</c> (which has real workflow rules: Draft/InReview/Rejected/Published/Archived with an
/// approval step), <see cref="PostStatus"/>'s own doc comment says this module has "no InReview/Rejected
/// admin-approval workflow" and does not enumerate which transitions are legal. Guessing at that transition
/// graph now (e.g. can a <see cref="PostStatus.Published"/> post go back to <see cref="PostStatus.Draft"/>?
/// does <see cref="PostStatus.Archived"/> → <see cref="PostStatus.Published"/> need a fresh
/// <see cref="Domain.POST.PUBLISHED_AT_UTC"/>?) would mean inventing business rules this scaffold pass is
/// not positioned to answer — same "do not guess at the real shape" restraint
/// <c>Siri.Modules.Payout.Application.PayoutBatchService</c>'s own doc comment applies to deliberately
/// leaving out an <c>ExecuteAsync</c> method entirely. <see cref="PostService.ChangeStatusAsync"/> is where
/// a later task adds real transition-legality rules once they are actually decided.
/// </para>
/// </summary>
public sealed record ChangePostStatusCommand(PostStatus Status);
