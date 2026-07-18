using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UpdateUserProfile;

public sealed class UpdateUserProfileCommandValidator : AbstractValidator<UpdateUserProfileCommand>
{
	public UpdateUserProfileCommandValidator()
	{
		RuleFor(x => x.Username)
			.NotEmpty()
			.WithMessage("Username is required")
			.Length(1, 255)
			.WithMessage("Username must be between 1 and 255 characters");

		RuleFor(x => x.TimezoneId)
			.GreaterThan(0)
			.WithMessage("TimezoneId must be greater than 0")
			.When(x => x.TimezoneId.HasValue);

		RuleFor(x => x.ProfilePictureUrl)
			.MaximumLength(500)
			.WithMessage("Profile picture URL cannot exceed 500 characters")
			.Must(BeAValidUrl)
			.WithMessage("Profile picture URL must be a valid URL")
			.When(x => !string.IsNullOrEmpty(x.ProfilePictureUrl));

		RuleFor(x => x.LanguageCode)
			.Length(2, 10)
			.WithMessage("Language code must be between 2 and 10 characters")
			.When(x => !string.IsNullOrWhiteSpace(x.LanguageCode));
	}

	private static bool BeAValidUrl(string? url)
	{
		if (string.IsNullOrEmpty(url)) return true;
		return Uri.TryCreate(url, UriKind.Absolute, out var result) &&
			   (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
	}
}
