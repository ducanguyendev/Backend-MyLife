using System.ComponentModel.DataAnnotations;

namespace LoginApp.Model
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
        [MinLength(2, ErrorMessage = "Họ và tên phải có ít nhất 2 ký tự.")]
        [MaxLength(100, ErrorMessage = "Họ và tên không được vượt quá 100 ký tự.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email là bắt buộc.")]
        [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng.")]
        [MaxLength(254, ErrorMessage = "Email không được vượt quá 254 ký tự.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
        [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
        [MaxLength(72, ErrorMessage = "Mật khẩu không được vượt quá 72 ký tự.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
