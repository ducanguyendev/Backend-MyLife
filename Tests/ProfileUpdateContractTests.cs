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

    [Theory]
    [InlineData("invalid", "Nam", 1995)]
    [InlineData("0123456789", "Nam", 1995)]
    [InlineData("0|12345678", "Nam", 1995)]
    [InlineData("0912345678", "male", 1995)]
    [InlineData("0912345678", "Nam", 2999)]
    public void Profile_update_rejects_invalid_mobile_gender_or_future_date(string phone, string gender, int year)
    {
        var model = new UpdateProfileViewModel
        {
            DisplayName = "Synthetic", PhoneNumber = phone, Gender = gender,
            DateOfBirth = new DateOnly(year, 1, 1)
        };
        Assert.NotEmpty(ValidationErrors(model));
    }
}
