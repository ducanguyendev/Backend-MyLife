using System.ComponentModel.DataAnnotations;

namespace LoginApp.Model
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Địa chỉ Email không đúng định dạng.")]
        [MaxLength(254, ErrorMessage = "Email tối đa 254 ký tự.")]
        [Display(Name = "Địa chỉ Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu không được để trống.")]
        [DataType(DataType.Password)]
        [StringLength(72, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ 8 đến 72 ký tự.")]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Ghi nhớ mật khẩu")]
        public bool RememberMe { get; set; }
    }
}