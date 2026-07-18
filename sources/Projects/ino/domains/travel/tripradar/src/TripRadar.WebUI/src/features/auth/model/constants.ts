export const AUTH_MESSAGES = {
  validation: {
    nameMin: 'Name must be at least 2 characters',
    emailInvalid: 'Invalid email address',
    passwordMin: 'Password must be at least 9 characters with uppercase, lowercase, digit and special character',
    passwordsNoMatch: "Passwords don't match",
  },
  passwordHints: {
    requirements: ['9+ characters', '1 uppercase', '1 number', '1 special char'],
  },
  ui: {
    createAccount: 'Create your account',
    subtitle: 'Plan amazing trips with ease',
    continueWith: 'Continue with',
    orContinueEmail: 'or continue with email',
    fullName: 'Full name',
    emailAddress: 'Email address',
    password: 'Password',
    confirmPassword: 'Confirm password',
    agreeToTerms: "I agree to TripRadar's",
    termsOfService: 'Terms',
    privacyPolicy: 'Privacy Policy',
    createAccountBtn: 'Get started',
    creatingAccount: 'Creating your account...',
    alreadyHaveAccount: 'Already have an account?',
    signIn: 'Sign in',
  },
  placeholders: {
    enterFullName: 'Enter your full name',
    enterEmail: 'Enter your email',
    createPassword: 'Create a password',
    confirmPassword: 'Confirm your password',
  },
} as const;
