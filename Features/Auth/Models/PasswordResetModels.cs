using System.ComponentModel.DataAnnotations;

namespace MyLife.Features.Auth.Models;

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng chuẩn.")]
    [MaxLength(254, ErrorMessage = "Email không được vượt quá 254 ký tự.")]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "Thiếu token đặt lại mật khẩu.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    [StringLength(72, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có độ dài tối thiểu từ 8 ký tự trở lên.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,72}$",
        ErrorMessage = "Mật khẩu bắt buộc chứa kết hợp chữ hoa, chữ thường, số và ít nhất một ký tự đặc biệt.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu xác nhận.")]
    [Compare("NewPassword", ErrorMessage = "Mật khẩu xác nhận phải khớp hoàn toàn với trường mật khẩu.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
