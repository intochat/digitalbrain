# UI components

The shared Flutter package is `digitalbrain_ui`, located in `src/Modules/UI/Flutter/ui`. Import `package:digitalbrain_ui/digitalbrain_ui.dart`. Its public widgets and models use the `Ui` prefix, including `UiChat`, `UiDataTable`, `UiTableController`, `UiTheme` and `UiGalleryScreen`.

UI neuron HTTP resources use `/ui`:

- `GET/POST /ui/tables`
- `GET /ui/tables/{id}`
- `PUT /ui/tables/{id}/view`
- `GET /ui/charts/{name}`
- `GET /ui/graphs/{name}`
- `GET /ui/images/{name}` and `/ui/images/{name}/content`
- `GET /ui/spreadsheets/{name}`
- `GET /ui/surfaces/{name}`

Clients and host must be upgraded together; the previous route prefix is not mapped as an alias. Authentication, table IDs, revisions, JSON shapes and neuron state are unchanged.

Two persisted identifiers intentionally retain their historical spelling: the Orleans `ui.kit-card` serialization alias and Azure `kit-images` container. Their source types are now `UiCardOffer` and `BlobUiImageStore`. Renaming either stored identifier requires an explicit data migration; changing them during a source rename would hide existing data. Third-party dependency names are also unchanged.

Run Flutter checks from `src/Modules/UI/Flutter` with `flutter analyze` and `flutter test core/test ui/test shell/test`. The Aspire watcher and CI use the same renamed directory.
