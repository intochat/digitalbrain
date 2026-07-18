import type { ReactNode } from 'react';
import { Elements } from '@stripe/react-stripe-js';
import { loadStripe } from '@stripe/stripe-js';
import { env } from 'shared/config';

const stripePromise = loadStripe(env.STRIPE_PUBLISHABLE_KEY);

interface StripeProviderProps {
  clientSecret: string;
  children: ReactNode;
}

export const StripeProvider = ({ clientSecret, children }: StripeProviderProps) => (
  <Elements stripe={stripePromise} options={{ clientSecret }}>
    {children}
  </Elements>
);
