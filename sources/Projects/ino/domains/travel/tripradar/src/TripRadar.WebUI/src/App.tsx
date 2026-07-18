import { AppLayout } from 'app/layout/AppLayout';
import { Providers } from 'app/providers';

export const App = () => {
  return (
    <Providers>
      <AppLayout />
    </Providers>
  );
};
