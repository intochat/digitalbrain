import { describe, expect, it } from 'vitest';
import { buildPostCheckoutReloadUrl, getBillingSnapshot, hasBillingSnapshotChanged } from './useCheckoutStatus';

describe('useCheckoutStatus helpers', () => {
  it('does not treat pre-existing billing data as refreshed when nothing changed', () => {
    const baseline = getBillingSnapshot(
      { status: 'active', nextInvoiceDate: '2026-05-05T00:00:00Z' },
      { paymentMethods: [{ id: 'pm_1', isDefault: true }] },
      {
        pages: [
          {
            invoices: [{ number: 'inv_1', status: 'paid', createdAt: '2026-04-05T00:00:00Z' }],
          },
        ],
      }
    );

    const current = getBillingSnapshot(
      { status: 'active', nextInvoiceDate: '2026-05-05T00:00:00Z' },
      { paymentMethods: [{ id: 'pm_1', isDefault: true }] },
      {
        pages: [
          {
            invoices: [{ number: 'inv_1', status: 'paid', createdAt: '2026-04-05T00:00:00Z' }],
          },
        ],
      }
    );

    expect(hasBillingSnapshotChanged(baseline, current)).toBe(false);
  });

  it('detects subscription refresh after checkout when billing data changes', () => {
    const baseline = getBillingSnapshot(
      { status: 'active', nextInvoiceDate: '2026-05-05T00:00:00Z' },
      { paymentMethods: [{ id: 'pm_1', isDefault: true }] },
      {
        pages: [
          {
            invoices: [{ number: 'inv_1', status: 'paid', createdAt: '2026-04-05T00:00:00Z' }],
          },
        ],
      }
    );

    const refreshed = getBillingSnapshot(
      { status: 'active', nextInvoiceDate: '2026-06-05T00:00:00Z' },
      { paymentMethods: [{ id: 'pm_1', isDefault: true }] },
      {
        pages: [
          {
            invoices: [{ number: 'inv_2', status: 'paid', createdAt: '2026-04-05T01:00:00Z' }],
          },
        ],
      }
    );

    expect(hasBillingSnapshotChanged(baseline, refreshed)).toBe(true);
  });
  it('detects subscription refresh when only the plan details change', () => {
    const baseline = getBillingSnapshot(
      {
        status: 'active',
        nextInvoiceDate: '2026-05-05T00:00:00Z',
        tierType: 'basic',
        billingPeriod: null,
        priceAmount: null,
        currency: null,
      },
      { paymentMethods: [{ id: 'pm_1', isDefault: true }] },
      {
        pages: [
          {
            invoices: [{ number: 'inv_1', status: 'paid', createdAt: '2026-04-05T00:00:00Z' }],
          },
        ],
      }
    );

    const refreshed = getBillingSnapshot(
      {
        status: 'active',
        nextInvoiceDate: '2026-05-05T00:00:00Z',
        tierType: 'essential',
        billingPeriod: 'monthly',
        priceAmount: 9,
        currency: 'USD',
      },
      { paymentMethods: [{ id: 'pm_1', isDefault: true }] },
      {
        pages: [
          {
            invoices: [{ number: 'inv_1', status: 'paid', createdAt: '2026-04-05T00:00:00Z' }],
          },
        ],
      }
    );

    expect(hasBillingSnapshotChanged(baseline, refreshed)).toBe(true);
  });

  it('builds a reload url without checkout params', () => {
    expect(
      buildPostCheckoutReloadUrl(
        '/profile/billing',
        new URLSearchParams('checkout=success&session_id=sess_123&foo=bar')
      )
    ).toBe('/profile/billing?foo=bar');
  });
});
