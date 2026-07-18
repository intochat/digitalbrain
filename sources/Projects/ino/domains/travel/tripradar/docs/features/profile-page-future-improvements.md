# Profile Page — Будущие доработки

Улучшения, выявленные при анализе конкурентов (Booking.com, KAYAK, Skyscanner, Hopper, TripAdvisor), но вынесенные из текущего скоупа.

## Требуют нового бэкенд-эндпоинта

### Загрузка аватара профиля
- Текущий `PUT /api/v1/users/profile` принимает `profilePictureUrl` как URL (макс. 500 символов), не поддерживает загрузку файлов или Data URL
- Нужно: добавить `POST /api/v1/users/profile/avatar` в `UsersController`, принимающий файл через `multipart/form-data`
- Бэкенд: сохранить файл в blob storage (Azure Blob Storage уже настроен — `BlobStorageUrl`, `BlobStorageSasToken`), записать полученный URL в `profilePictureUrl` профиля
- Фронтенд: компонент `AvatarSection` уже реализован (`src/pages/profile/ui/AvatarSection.tsx`), нужно заменить `readAsDataURL` на `FormData` + `fetch` к новому эндпоинту
- Валидация: JPEG/PNG/WebP, до 5 МБ (фронтенд-валидация уже есть)

### Страна проживания
- Таблица `Countries` и `ICountryRepository` уже есть в бэкенде
- Нужно: добавить `GET /api/v1/portal/countries` в `PortalController` (по аналогии с `/portal/languages`, `/portal/timezones`)
- Фронтенд: `usePortalCountriesQuery` хук + `Dropdown` в секции Preferences на `/profile`
- `countryCode` уже поддерживается в `UpdateUserProfileRequest` и `GetUserProfileResponse`

## Требуют новых полей в доменной модели

### Предпочитаемая валюта
- Новое поле `currencyCode` в `UserProfile`
- Dropdown на странице профиля (эндпоинт `/portal/currencies` уже есть)
- Влияет на отображение цен в поиске

### Дата рождения
- Новое поле `dateOfBirth` в `UserProfile`
- Date picker на странице профиля
- Автозаполнение при бронировании авиабилетов (возрастные категории)

### Пол
- Новое поле `gender` в `UserProfile`
- Dropdown (Mr/Mrs/Ms) на странице профиля
- Автозаполнение при бронировании (обращение в документах)
