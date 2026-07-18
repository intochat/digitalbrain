import { useFrontendLanguage } from 'app/providers';

interface ResponsiveTableProps {
  headers: string[];
  rows: Record<string, string>[];
}

export const ResponsiveTable = ({ headers, rows }: ResponsiveTableProps) => {
  const { t } = useFrontendLanguage();

  return (
    <>
      {/* Desktop: standard table */}
      <div className="hidden md:block overflow-x-auto">
        <table className="min-w-full text-sm border border-outline dark:border-outline-dark rounded-lg overflow-hidden">
          <thead className="bg-surface dark:bg-surface-dark-secondary">
            <tr>
              {headers.map(header => (
                <th
                  key={header}
                  className="text-left px-3 py-2 font-medium text-content dark:text-content-dark border-b border-outline dark:border-outline-dark"
                >
                  {t(header)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, rowIdx) => (
              <tr
                key={rowIdx}
                className={`align-top ${rowIdx % 2 === 1 ? 'bg-surface-accent/50 dark:bg-surface-accent-dark/50' : ''}`}
              >
                {headers.map((header, colIdx) => (
                  <td
                    key={header}
                    className={`px-3 py-2 border-b border-outline dark:border-outline-dark ${
                      colIdx === 0
                        ? 'text-content dark:text-content-dark'
                        : 'text-content-secondary dark:text-content-secondary-dark'
                    }`}
                  >
                    {t(row[header] ?? '')}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Mobile: card layout */}
      <div className="md:hidden space-y-3">
        {rows.map((row, rowIdx) => (
          <div
            key={rowIdx}
            className="rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark p-4 space-y-2"
          >
            {headers.map(header => (
              <div key={header}>
                <span className="text-xs font-medium uppercase tracking-wide text-content-secondary dark:text-content-secondary-dark">
                  {t(header)}
                </span>
                <p className="text-sm text-content dark:text-content-dark mt-0.5">{t(row[header] ?? '')}</p>
              </div>
            ))}
          </div>
        ))}
      </div>
    </>
  );
};
