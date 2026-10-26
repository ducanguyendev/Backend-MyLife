using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyLife.Features.FamilyTree.Controllers;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Features.FamilyTree.Services;
using MyLife.Shared.Entities;
using MyLife.Shared.Security;
using Xunit;
using UserFamilyTree = MyLife.Shared.Entities.FamilyTree;

public sealed class FamilyTreeAvatarConditionalRequestTests
{
    [Fact]
    public async Task Old_etag_after_replace_returns_new_bytes_and_current_etag_returns_304()
    {
        const string oldEtag = "\"revision-a\"";
        const string currentEtag = "\"revision-b\"";
        var service = new AvatarImageService(new MemberAvatarImageResult([255, 216, 255, 9], "image/jpeg", currentEtag));
        var controller = new FamilyTreeController(service, new AuthenticatedUser());
        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        context.Request.Headers.IfNoneMatch = oldEtag;
        var changedResult = await controller.GetAvatarImage(15);
        var image = Assert.IsType<FileContentResult>(changedResult);
        Assert.Equal(new byte[] { 255, 216, 255, 9 }, image.FileContents);
        Assert.Equal(currentEtag, context.Response.Headers.ETag);
        Assert.Equal("private, no-cache", context.Response.Headers.CacheControl);

        context.Request.Headers.IfNoneMatch = currentEtag;
        var unchangedResult = await controller.GetAvatarImage(15);
        Assert.Equal(StatusCodes.Status304NotModified, Assert.IsType<StatusCodeResult>(unchangedResult).StatusCode);
    }

    private sealed class AuthenticatedUser : ICurrentUserService
    {
        public Task<int?> GetUserIdAsync(CancellationToken ct) => Task.FromResult<int?>(1);
    }

    private sealed class AvatarImageService(MemberAvatarImageResult image) : IFamilyTreeService
    {
        public Task<MemberAvatarImageResult?> GetMemberAvatarImageAsync(int userId, int memberId, CancellationToken ct) => Task.FromResult<MemberAvatarImageResult?>(image);
        public Task<UserFamilyTree> EnsureUserFamilyTreeAsync(int userId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IEnumerable<FamilyMemberDto>> GetAllMembersAsync(int userId, CancellationToken ct) => throw new NotImplementedException();
        public Task<FamilyMemberDto?> GetMemberByIdAsync(int userId, int memberId, CancellationToken ct) => throw new NotImplementedException();
        public Task<FamilyMemberDto> CreateMemberAsync(int userId, CreateFamilyMemberDto dto, CancellationToken ct) => throw new NotImplementedException();
        public Task<FamilyMemberDto> UpdateMemberAsync(int userId, int memberId, UpdateFamilyMemberDto dto, CancellationToken ct) => throw new NotImplementedException();
        public Task<FamilyMemberDto> UpdateMemberLocationAsync(int userId, int memberId, UpdateFamilyMemberLocationDto dto, CancellationToken ct) => throw new NotImplementedException();
        public Task<FamilyMemberDto> UploadMemberAvatarAsync(int userId, int memberId, byte[] content, string contentType, CancellationToken ct) => throw new NotImplementedException();
        public Task<FamilyMemberDto> DeleteMemberAvatarAsync(int userId, int memberId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> DeleteMemberAsync(int userId, int memberId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IEnumerable<GenerationDto>> GetAllGenerationsAsync(CancellationToken ct) => throw new NotImplementedException();
    }
}
