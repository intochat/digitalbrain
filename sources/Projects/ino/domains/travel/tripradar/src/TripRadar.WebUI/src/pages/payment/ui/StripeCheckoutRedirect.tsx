import { useEffect, useRef } from 'react';
import { Navigate, useSearchParams } from 'react-router-dom';
import { ROUTES } from 'shared/config/routes';
import { trackEvent } from 'shared/lib';
import { useAuthStore } from 'shared/store/auth';

type CheckoutStatus = 'success' | 'cancel';

interface StripeCheckoutRedirectProps {
  status: CheckoutStatus;
}

export const StripeCheckoutRedirect = ({ status }: StripeCheckoutRedirectProps) => {
  const { user } = useAuthStore();
  const [searchParams] = useSearchParams();
  const sessionId = searchParams.get('session_id');
  const redirectStartedRef = useRef(false);

  const checkoutStatusQuery = status === 'success' ? 'success' : 'cancel';
  const destination = sessionId
    ? `${ROUTES.PROFILE_BILLING}?checkout=${checkoutStatusQuery}&session_id=${encodeURIComponent(sessionId)}`
    : `${ROUTES.PROFILE_BILLING}?checkout=${checkoutStatusQuery}`;

  useEffect(() => {
    if (!user?.username || redirectStartedRef.current) {
      return;
    }

    redirectStartedRef.current = true;

    if (status === 'success') {
      trackEvent(
        'checkout_completed',
        {
          sessionIdPresent: Boolean(sessionId),
        },
        { stage: 'revenue', userState: 'paid' }
      );
      trackEvent(
        'paid_conversion',
        {
          source: 'stripe_checkout_redirect',
          sessionIdPresent: Boolean(sessionId),
        },
        { stage: 'revenue', userState: 'paid' }
      );
    } else {
      trackEvent(
        'checkout_canceled',
        {
          sessionIdPresent: Boolean(sessionId),
        },
        { stage: 'revenue', userState: 'activated' }
      );
    }

    window.location.replace(destination);
  }, [destination, sessionId, status, user?.username]);

  if (!user?.username) {
    return <Navigate to={ROUTES.LOGIN} replace />;
  }

  return null;
};
