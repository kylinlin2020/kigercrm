import { beforeEach, describe, expect, it } from 'vitest'
import { migrateBrandStorage, migrateBrandValue } from './brand-migration'

describe('brand migration', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('maps legacy brand strings to the new default', () => {
    expect(migrateBrandValue('XiHan BasicApp', 'KIGER CRM')).toBe('KIGER CRM')
    expect(migrateBrandValue('XiHan Admin', 'KIGER CRM')).toBe('KIGER CRM')
    expect(migrateBrandValue('XiHan', 'KIGER')).toBe('KIGER')
    expect(migrateBrandValue('https://www.xihanfun.com', 'https://kiger')).toBe('https://github.com/kylinlin2020/kigercrm')
  })

  it('keeps user-defined values and fills missing values with the default', () => {
    expect(migrateBrandValue('我的CRM', 'KIGER CRM')).toBe('我的CRM')
    expect(migrateBrandValue('', 'KIGER CRM')).toBe('')
    expect(migrateBrandValue(null, 'KIGER CRM')).toBe('KIGER CRM')
    expect(migrateBrandValue(undefined, 'KIGER CRM')).toBe('KIGER CRM')
  })

  it('writes the migrated value back to storage as JSON when a key is provided', () => {
    const key = 'test_brand_migration'
    localStorage.setItem(key, JSON.stringify('XiHan BasicApp'))
    expect(migrateBrandValue('XiHan BasicApp', 'KIGER CRM', key)).toBe('KIGER CRM')
    expect(JSON.parse(localStorage.getItem(key) ?? '')).toBe('KIGER CRM')
  })

  it('rewrites all legacy brand values across localStorage at boot', () => {
    localStorage.setItem('xihan_brand_title', JSON.stringify('XiHan BasicApp'))
    localStorage.setItem('xihan_brand_title2', JSON.stringify('XiHan Admin'))
    localStorage.setItem('xihan_copyright_name', JSON.stringify('XiHan'))
    localStorage.setItem('xihan_copyright_site', JSON.stringify('https://www.xihanfun.com'))
    localStorage.setItem('xihan_user_name', JSON.stringify('张三'))
    migrateBrandStorage()
    expect(JSON.parse(localStorage.getItem('xihan_brand_title') ?? '')).toBe('KIGER CRM')
    expect(JSON.parse(localStorage.getItem('xihan_brand_title2') ?? '')).toBe('KIGER CRM')
    expect(JSON.parse(localStorage.getItem('xihan_copyright_name') ?? '')).toBe('KIGER')
    expect(JSON.parse(localStorage.getItem('xihan_copyright_site') ?? '')).toBe('https://github.com/kylinlin2020/kigercrm')
    expect(JSON.parse(localStorage.getItem('xihan_user_name') ?? '')).toBe('张三')
  })
})
