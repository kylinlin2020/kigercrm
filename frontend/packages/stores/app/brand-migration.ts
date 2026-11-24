/**
 * One-time migration for brand strings saved in localStorage before the KIGER CRM rebrand.
 * Old values ('XiHan BasicApp', 'XiHan Admin', 'XiHan') are replaced with the new defaults so
 * existing users see the new brand without clearing their settings. Only brand fields are touched.
 */
const LEGACY_BRAND_VALUES = new Set([
  'XiHan BasicApp',
  'XiHan Admin',
  'XiHanBasicApp',
  'XiHan',
  'https://www.xihanfun.com',
  'https://basicapp.xihanfun.com',
])

const NEW_BRAND_VALUES: Record<string, string> = {
  'XiHan BasicApp': 'KIGER CRM',
  'XiHan Admin': 'KIGER CRM',
  'XiHanBasicApp': 'KIGER CRM',
  'XiHan': 'KIGER',
  'https://www.xihanfun.com': 'https://github.com/kylinlin2020/kigercrm',
  'https://basicapp.xihanfun.com': 'https://github.com/kylinlin2020/kigercrm',
}

function isLegacyBrand(value: string): boolean {
  return LEGACY_BRAND_VALUES.has(value)
    || value.includes('XiHan')
    || value.includes('xihanfun.com')
}

function replacementFor(value: string, fallback: string): string {
  return NEW_BRAND_VALUES[value] ?? fallback
}

export function migrateBrandValue(
  stored: string | null | undefined,
  fallback: string,
  storageKey?: string,
): string {
  if (typeof stored === 'string' && isLegacyBrand(stored)) {
    const replacement = replacementFor(stored, fallback)
    if (storageKey)
      localStorage.setItem(storageKey, JSON.stringify(replacement))
    return replacement
  }
  return stored ?? fallback
}

/**
 * Scan every localStorage entry once at app boot and rewrite legacy brand values.
 * Runs before Pinia stores initialize, so all stores read migrated values.
 */
const BRAND_KEY_PATTERN = /brand|copyright|watermark|app_title/i

export function migrateBrandStorage(): void {
  for (let i = 0; i < localStorage.length; i++) {
    const key = localStorage.key(i)
    if (!key || !BRAND_KEY_PATTERN.test(key))
      continue
    const raw = localStorage.getItem(key)
    if (!raw)
      continue
    let value: unknown
    try {
      value = JSON.parse(raw)
    }
    catch {
      continue
    }
    if (typeof value === 'string' && isLegacyBrand(value)) {
      localStorage.setItem(key, JSON.stringify(replacementFor(value, 'KIGER CRM')))
    }
  }
}
