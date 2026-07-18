import { useLocation } from 'react-router-dom';
import { Header, Footer } from 'widgets';
import { ROUTES } from 'shared/config/routes';
import { ErrorBoundary } from 'shared/ui';
import { AppRoutes } from '../router/routes';
import { ScrollToTop } from '../router/ScrollToTop';

export const AppLayout = () => {
  const location = useLocation();
  const isImmersiveCheckoutRoute = location.pathname === ROUTES.SUBSCRIPTION_CHECKOUT;

  return (
    <div className="min-h-screen flex flex-col bg-surface-accent dark:bg-surface-dark transition-colors duration-150">
      <ScrollToTop />
      {!isImmersiveCheckoutRoute && <Header />}

      <main id="main-content" className="flex-1 flex flex-col">
        <ErrorBoundary>
          <AppRoutes />
        </ErrorBoundary>
      </main>

      {!isImmersiveCheckoutRoute && <Footer />}
    </div>
  );
};
