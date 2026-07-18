import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { CancelSubscriptionDialog } from 'features/payment';
import { ROUTES } from 'shared/config/routes';
import { InvoiceHistorySection } from './billing/InvoiceHistorySection';
import { PaymentMethodsSection } from './billing/PaymentMethodsSection';
import { SubscriptionSection } from './billing/SubscriptionSection';
import { useCheckoutStatus } from './billing/useCheckoutStatus';
import { useTierInfo } from './billing/useTierInfo';
import { ProfileLayout } from './ProfileLayout';

export const ProfileBilling = () => {
  useCheckoutStatus();
  const navigate = useNavigate();
  const { tierName, subscription } = useTierInfo();

  const [isCancelOpen, setIsCancelOpen] = useState(false);

  const handleSwitchToChangePlan = () => {
    setIsCancelOpen(false);
    navigate(ROUTES.PRICING + '?from=billing');
  };

  return (
    <ProfileLayout>
      <div className="px-4 sm:px-6 lg:px-8 pb-4 sm:pb-6 lg:pb-8">
        <div className="pb-8 border-b border-outline/40 dark:border-outline-dark/40">
          <SubscriptionSection onCancelSubscription={() => setIsCancelOpen(true)} />
        </div>
        <div className="space-y-10 pt-8">
          <PaymentMethodsSection />
          <InvoiceHistorySection />
        </div>
      </div>

      <CancelSubscriptionDialog
        isOpen={isCancelOpen}
        onClose={() => setIsCancelOpen(false)}
        currentPeriodEnd={subscription?.currentPeriodEnd}
        currentTierType={tierName}
        onSwitchToChangePlan={handleSwitchToChangePlan}
      />
    </ProfileLayout>
  );
};
