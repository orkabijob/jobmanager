using Microsoft.AspNetCore.Identity;

namespace Orkabi.Web.Modules.Identity;

/// <summary>
/// TD1 — Hebrew translations for the ASP.NET Core Identity errors that surface in the UI:
/// user create/edit on /Admin/Users, registration, and change/reset password. Anything not
/// overridden falls back to the English base, but these cover every error reachable today.
/// </summary>
public class HebrewIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = "אירעה שגיאה לא צפויה." };

    public override IdentityError ConcurrencyFailure() =>
        new() { Code = nameof(ConcurrencyFailure), Description = "הנתונים שונו על-ידי משתמש אחר. רעננו ונסו שוב." };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = $"כתובת הדוא\"ל '{email}' כבר בשימוש." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = $"שם המשתמש '{userName}' כבר תפוס." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = $"כתובת הדוא\"ל '{email}' אינה תקינה." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = $"שם המשתמש '{userName}' אינו תקין." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"הסיסמה חייבת להכיל לפחות {length} תווים." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "הסיסמה הנוכחית שגויה." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "הסיסמה חייבת להכיל ספרה." };

    public override IdentityError PasswordRequiresLower() =>
        new() { Code = nameof(PasswordRequiresLower), Description = "הסיסמה חייבת להכיל אות קטנה." };

    public override IdentityError PasswordRequiresUpper() =>
        new() { Code = nameof(PasswordRequiresUpper), Description = "הסיסמה חייבת להכיל אות גדולה." };

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "הסיסמה חייבת להכיל תו מיוחד (לא אות ולא ספרה)." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"הסיסמה חייבת להכיל לפחות {uniqueChars} תווים שונים." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "האסימון אינו תקין או שפג תוקפו." };

    public override IdentityError UserAlreadyHasPassword() =>
        new() { Code = nameof(UserAlreadyHasPassword), Description = "למשתמש כבר מוגדרת סיסמה." };

    public override IdentityError UserAlreadyInRole(string role) =>
        new() { Code = nameof(UserAlreadyInRole), Description = $"המשתמש כבר משויך לתפקיד '{role}'." };

    public override IdentityError UserNotInRole(string role) =>
        new() { Code = nameof(UserNotInRole), Description = $"המשתמש אינו משויך לתפקיד '{role}'." };
}
