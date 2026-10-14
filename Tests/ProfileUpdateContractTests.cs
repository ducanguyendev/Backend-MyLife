using System.ComponentModel.DataAnnotations;
using MyLife.Features.Auth.Models;
using MyLife.Features.User.Models;
using Xunit;

public sealed class ProfileUpdateContractTests
{
    [Fact]
    public void Profile_update_accepts_editable_fields_without_a_full_name_field()
    {
        Assert.Null(typeof(UpdateProfileViewModel).GetProperty("FullName"));

        var model = new UpdateProfileViewModel
        {
            DisplayName = "Dukaa",
            PhoneNumber = "0987654321",
            Gender = "Nữ",
            DateOfBirth = new DateOnly(1995, 5, 21)
        };

        Assert.Empty(ValidationErrors(model));
    }

    [Fact]
    public void Registration_still_requires_full_name()
    {
        var model = new RegisterViewModel
        {
            PhoneNumber = "0912345678",
            Gender = "Nam",
            DateOfBirth = new DateOnly(1995, 5, 20),
            Email = "register@gmail.com",
            Password = "User_Test!234567",
            ConfirmPassword = "User_Test!234567"
        };

        Assert.Contains(ValidationErrors(model), error => error.MemberNames.Contains(nameof(RegisterViewModel.FullName)));
    }

    private static List<ValidationResult> ValidationErrors(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
