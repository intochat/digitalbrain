import '@testing-library/jest-dom';

const createMemoryStorage = (): Storage => {
  const store = new Map<string, string>();

  return {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: key => (store.has(key) ? store.get(key)! : null),
    key: index => Array.from(store.keys())[index] ?? null,
    removeItem: key => store.delete(key),
    setItem: (key, value) => {
      store.set(String(key), String(value));
    },
  };
};

const ensureStorage = (storageKey: 'localStorage' | 'sessionStorage') => {
  const existingStorage = window[storageKey];
  if (
    existingStorage &&
    typeof existingStorage.getItem === 'function' &&
    typeof existingStorage.setItem === 'function'
  ) {
    return;
  }

  Object.defineProperty(window, storageKey, {
    configurable: true,
    value: createMemoryStorage(),
  });
};

ensureStorage('localStorage');
ensureStorage('sessionStorage');

await import('app/i18n');
