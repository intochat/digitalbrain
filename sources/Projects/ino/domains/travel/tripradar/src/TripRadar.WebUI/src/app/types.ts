export interface User {
  username: string;
  name: string;
  email: string;
  avatar: string;
  subscription: 'free' | 'premium' | 'enterprise';
}
