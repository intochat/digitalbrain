import { Navigate, Route, Routes } from 'react-router-dom';
import { AuthGuard, Login, ProtectedRoute, Signup } from 'features/auth';
import {
  ConfirmEmail,
  EmailConfirmed,
  EmailSent,
  ForgotPassword,
  ResetPassword,
  SessionHandoff,
  TelegramCallback,
  TelegramGoogleAuth,
  TelegramUsernameSync,
} from 'pages/auth';
import { Feedback } from 'pages/feedback';
import {
  AlternativesGuide,
  BudgetAiPlannerGuide,
  BudgetGuide2026,
  Changelog,
  ChecklistTemplateGuide,
  ExampleTripPlanGuide,
  Home,
  ManualVsTripRadarGuide,
  NotFound,
  Pricing,
  SavingsMethodologyGuide,
  TelegramTripPlannerGuide,
  Unsubscribe,
} from 'pages/marketing';
import { SubscriptionCheckout, StripeCheckoutRedirect } from 'pages/payment';
import {
  Profile,
  ProfileBilling,
  ProfilePreferences,
  ProfileScheduledRequests,
  ProfileSecurity,
  ProfileTripHistory,
  ProfileTrips,
  ProfileUsage,
} from 'pages/profile';
import { CookiePolicy, HelpCenter, PrivacyPolicy, TermsOfService } from 'shared/ui/legal';

export const AppRoutes = () => {
  return (
    <Routes>
      <Route path="/signin" element={<Login />} />
      <Route path="/login" element={<Navigate to="/signin" replace />} />
      <Route
        path="/signup"
        element={
          <AuthGuard>
            <Signup />
          </AuthGuard>
        }
      />
      <Route path="/email-sent" element={<EmailSent />} />
      <Route path="/confirm-email" element={<ConfirmEmail />} />
      <Route path="/email-confirmed" element={<EmailConfirmed />} />
      <Route
        path="/forgot-password"
        element={
          <AuthGuard>
            <ForgotPassword />
          </AuthGuard>
        }
      />
      <Route
        path="/reset-password"
        element={
          <AuthGuard>
            <ResetPassword />
          </AuthGuard>
        }
      />
      <Route path="/auth/telegram-callback" element={<TelegramCallback />} />
      <Route path="/auth/telegram-google" element={<TelegramGoogleAuth />} />
      <Route path="/auth/telegram-username-sync" element={<TelegramUsernameSync />} />
      <Route path="/auth/session-handoff" element={<SessionHandoff />} />
      <Route path="/payment/success" element={<StripeCheckoutRedirect status="success" />} />
      <Route path="/payment/cancel" element={<StripeCheckoutRedirect status="cancel" />} />
      <Route
        path="/subscription/checkout"
        element={
          <ProtectedRoute>
            <SubscriptionCheckout />
          </ProtectedRoute>
        }
      />
      <Route path="/subscription/success" element={<StripeCheckoutRedirect status="success" />} />
      <Route path="/subscription/cancel" element={<StripeCheckoutRedirect status="cancel" />} />
      <Route path="/" element={<Home />} />
      <Route path="/pricing" element={<Pricing />} />
      <Route path="/changelog" element={<Changelog />} />
      <Route path="/unsubscribe" element={<Unsubscribe />} />
      <Route path="/telegram-trip-planner" element={<TelegramTripPlannerGuide />} />
      <Route path="/ai-trip-planner-budget" element={<BudgetAiPlannerGuide />} />
      <Route path="/trip-planning-assistant-alternatives" element={<AlternativesGuide />} />
      <Route path="/trip-budget-guide-2026" element={<BudgetGuide2026 />} />
      <Route path="/trip-checklist-template" element={<ChecklistTemplateGuide />} />
      <Route path="/manual-planning-vs-tripradar" element={<ManualVsTripRadarGuide />} />
      <Route path="/example-trip-plan" element={<ExampleTripPlanGuide />} />
      <Route path="/savings-methodology" element={<SavingsMethodologyGuide />} />
      <Route path="/feedback" element={<Feedback />} />
      <Route path="/help" element={<HelpCenter />} />
      <Route path="/cookies" element={<CookiePolicy />} />
      <Route path="/privacy" element={<PrivacyPolicy />} />
      <Route path="/terms" element={<TermsOfService />} />

      <Route
        path="/profile"
        element={
          <ProtectedRoute>
            <Profile />
          </ProtectedRoute>
        }
      />
      <Route
        path="/profile/security"
        element={
          <ProtectedRoute>
            <ProfileSecurity />
          </ProtectedRoute>
        }
      />
      <Route
        path="/profile/billing"
        element={
          <ProtectedRoute>
            <ProfileBilling />
          </ProtectedRoute>
        }
      />
      <Route
        path="/profile/usage"
        element={
          <ProtectedRoute>
            <ProfileUsage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/profile/preferences"
        element={
          <ProtectedRoute>
            <ProfilePreferences />
          </ProtectedRoute>
        }
      />
      <Route path="/profile/privacy" element={<Navigate to="/profile/security" replace />} />
      <Route
        path="/profile/scheduled-requests"
        element={
          <ProtectedRoute>
            <ProfileScheduledRequests />
          </ProtectedRoute>
        }
      />
      <Route
        path="/profile/trips"
        element={
          <ProtectedRoute>
            <ProfileTrips />
          </ProtectedRoute>
        }
      />
      <Route
        path="/profile/trips/:tripUniqueId/history"
        element={
          <ProtectedRoute>
            <ProfileTripHistory />
          </ProtectedRoute>
        }
      />
      <Route path="*" element={<NotFound />} />
    </Routes>
  );
};
