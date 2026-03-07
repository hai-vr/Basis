using System.Text;
using Basis.Network.Server.Auth;
using Xunit;

namespace BasisServerTests
{
    public class PasswordAuthTests
    {
        [Fact]
        public void IsAuthenticated_CorrectPassword_ReturnsTrue()
        {
            var auth = new PasswordAuth("my_secret");
            byte[] passwordBytes = Encoding.UTF8.GetBytes("my_secret");
            Assert.True(auth.IsAuthenticated(passwordBytes));
        }

        [Fact]
        public void IsAuthenticated_WrongPassword_ReturnsFalse()
        {
            var auth = new PasswordAuth("my_secret");
            byte[] passwordBytes = Encoding.UTF8.GetBytes("wrong_password");
            Assert.False(auth.IsAuthenticated(passwordBytes));
        }

        [Fact]
        public void IsAuthenticated_EmptyServerPassword_AlwaysAllows()
        {
            var auth = new PasswordAuth("");
            byte[] passwordBytes = Encoding.UTF8.GetBytes("anything");
            Assert.True(auth.IsAuthenticated(passwordBytes));
        }

        [Fact]
        public void IsAuthenticated_NullServerPassword_AlwaysAllows()
        {
            var auth = new PasswordAuth(null);
            byte[] passwordBytes = Encoding.UTF8.GetBytes("anything");
            Assert.True(auth.IsAuthenticated(passwordBytes));
        }

        [Fact]
        public void IsAuthenticated_EmptyUserPassword_WithServerPassword_ReturnsFalse()
        {
            var auth = new PasswordAuth("secret");
            byte[] passwordBytes = Encoding.UTF8.GetBytes("");
            Assert.False(auth.IsAuthenticated(passwordBytes));
        }

        [Fact]
        public void IsAuthenticated_CaseSensitive()
        {
            var auth = new PasswordAuth("Password");
            byte[] passwordBytes = Encoding.UTF8.GetBytes("password");
            Assert.False(auth.IsAuthenticated(passwordBytes));
        }

        [Fact]
        public void IsAuthenticated_UnicodePassword()
        {
            var auth = new PasswordAuth("пароль123");
            byte[] correct = Encoding.UTF8.GetBytes("пароль123");
            byte[] wrong = Encoding.UTF8.GetBytes("пароль124");

            Assert.True(auth.IsAuthenticated(correct));
            Assert.False(auth.IsAuthenticated(wrong));
        }

        [Fact]
        public void IsAuthenticated_SpecialCharacters()
        {
            var auth = new PasswordAuth("p@$$w0rd!#%^&*()");
            byte[] passwordBytes = Encoding.UTF8.GetBytes("p@$$w0rd!#%^&*()");
            Assert.True(auth.IsAuthenticated(passwordBytes));
        }

        [Fact]
        public void IsAuthenticated_LongPassword()
        {
            string longPass = new string('A', 10000);
            var auth = new PasswordAuth(longPass);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(longPass);
            Assert.True(auth.IsAuthenticated(passwordBytes));
        }
    }
}
