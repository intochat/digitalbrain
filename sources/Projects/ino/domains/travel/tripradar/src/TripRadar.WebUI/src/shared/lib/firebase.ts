import { initializeApp } from 'firebase/app';
import { getAuth, type Auth } from 'firebase/auth';
import { env, isFirebaseAuthConfigured } from 'shared/config';

const firebaseConfig = {
  apiKey: env.FIREBASE_API_KEY,
  authDomain: env.FIREBASE_AUTH_DOMAIN,
  projectId: env.FIREBASE_PROJECT_ID,
  storageBucket: env.FIREBASE_STORAGE_BUCKET,
  messagingSenderId: env.FIREBASE_MESSAGING_SENDER_ID,
  appId: env.FIREBASE_APP_ID,
  measurementId: env.FIREBASE_MEASUREMENT_ID,
};

let auth: Auth | null = null;

if (isFirebaseAuthConfigured()) {
  const app = initializeApp(firebaseConfig);
  auth = getAuth(app);
} else if (import.meta.env.DEV) {
  console.warn('Firebase configuration is missing. Google sign-in is disabled.');
}

export { auth };
