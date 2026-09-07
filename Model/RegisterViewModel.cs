using System.ComponentModel.DataAnnotations;

namespace LoginApp.Model
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Họ và tên phải có độ dài từ 2 đến 50 ký tự.")]
        [RegularExpression(@"^[\p{L}\s]+$", ErrorMessage = "Họ và tên không được chứa số hoặc ký tự đặc biệt.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
        [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng chuẩn.")]
        [MaxLength(254, ErrorMessage = "Email không được vượt quá 254 ký tự.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [StringLength(72, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có độ dài tối thiểu từ 8 ký tự trở lên.")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,72}$", 
            ErrorMessage = "Mật khẩu bắt buộc chứa kết hợp chữ hoa, chữ thường, số và ít nhất một ký tự đặc biệt.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu xác nhận.")]
        [Compare("Password", ErrorMessage = "Mật khẩu xác nhận phải khớp hoàn toàn với trường mật khẩu.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
