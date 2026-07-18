import type { PropsWithChildren } from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { AppRoutes } from './routes';

vi.mock('features/auth', () => ({
  AuthGuard: ({ children }: PropsWithChildren) => <>{children}</>,
  Login: () => <div>Login page</div>,
  ProtectedRoute: ({ children }: PropsWithChildren) => <>{children}</>,
  Signup: () => <div>Signup page</div>,
}));

vi.mock('pages/auth', () => ({
  ConfirmEmail: () => <div>Confirm email</div>,
  EmailConfirmed: () => <div>Email confirmed</div>,
  EmailSent: () => <div>Email sent</div>,
  ForgotPassword: () => <div>Forgot password</div>,
  ResetPassword: () => <div>Reset password</div>,
  TelegramCallback: () => <div>Telegram callback</div>,
  TelegramUsernameSync: () => <div>Telegram username sync</div>,
}));

vi.mock('pages/feedback', () => ({
  Feedback: () => <div>Feedback page</div>,
}));

vi.mock('pages/marketing', () => ({
  AlternativesGuide: () => <div>Alternatives guide</div>,
  BudgetAiPlannerGuide: () => <div>Budget AI planner guide</div>,
  BudgetGuide2026: () => <div>Budget guide 2026</div>,
  Changelog: () => <div>Changelog page</div>,
  ChecklistTemplateGuide: () => <div>Checklist template guide</div>,
  ExampleTripPlanGuide: () => <div>Example trip plan guide</div>,
  Home: () => <div>Home page</div>,
  ManualVsTripRadarGuide: () => <div>Manual vs TripRadar guide</div>,
  NotFound: () => <div>Not found page</div>,
  Pricing: () => <div>Pricing page</div>,
  SavingsMethodologyGuide: () => <div>Savings methodology guide</div>,
  TelegramTripPlannerGuide: () => <div>Telegram trip planner guide</div>,
  Unsubscribe: () => <div>Unsubscribe page</div>,
}));

vi.mock('pages/payment', () => ({
  StripeCheckoutRedirect: ({ status }: { status: string }) => <div>{status}</div>,
  SubscriptionCheckout: () => <div>Subscription checkout</div>,
}));

vi.mock('pages/profile', () => ({
  Profile: () => <div>Profile page</div>,
  ProfileBilling: () => <div>Profile billing</div>,
  ProfilePreferences: () => <div>Profile preferences</div>,
  ProfileScheduledRequests: () => <div>Profile scheduled requests</div>,
  ProfileSecurity: () => <div>Profile security</div>,
  ProfileTripHistory: () => <div>Profile trip history</div>,
  ProfileTrips: () => <div>Profile trips</div>,
  ProfileUsage: () => <div>Profile usage</div>,
}));

vi.mock('shared/ui/legal', () => ({
  CookiePolicy: () => <div>Cookie policy</div>,
  HelpCenter: () => <div>Help center</div>,
  PrivacyPolicy: () => <div>Privacy policy</div>,
  TermsOfService: () => <div>Terms of service</div>,
}));

describe('AppRoutes', () => {
  it('renders the changelog page on /changelog', () => {
    render(
      <MemoryRouter initialEntries={['/changelog']}>
        <AppRoutes />
      </MemoryRouter>
    );

    expect(screen.getByText('Changelog page')).toBeInTheDocument();
  });
});
