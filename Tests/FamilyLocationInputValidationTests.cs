using System.ComponentModel.DataAnnotations;
using MyLife.Features.FamilyTree.DTOs;
using Xunit;

public sealed class FamilyLocationInputValidationTests
{
    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    [InlineData(double.MaxValue, 0)]
    [InlineData(90.1, 0)]
    [InlineData(-90.1, 0)]
    [InlineData(0, 180.1)]
    [InlineData(0, -180.1)]
    public void Invalid_coordinates_fail_validation_without_throwing(double latitude, double longitude)
    {
        var dto = new UpdateFamilyMemberLocationDto { Latitude = latitude, Longitude = longitude };
        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), [], true));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    public void Zero_and_boundary_coordinates_pass_validation(double latitude, double longitude)
    {
        var dto = new UpdateFamilyMemberLocationDto { Latitude = latitude, Longitude = longitude };
        Assert.True(Validator.TryValidateObject(dto, new ValidationContext(dto), [], true));
    }
}
