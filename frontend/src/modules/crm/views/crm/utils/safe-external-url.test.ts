import { describe, expect, it } from 'vitest'
import { safeExternalUrl } from './safe-external-url'

describe('safeExternalUrl', () => {
  it('allows http(s) URLs and normalizes them', () => {
    expect(safeExternalUrl('https://example.com/path')).toBe('https://example.com/path')
    expect(safeExternalUrl('http://example.com')).toBe('http://example.com/')
  })

  it('rejects non-network schemes that could execute script or read local data', () => {
    expect(safeExternalUrl('javascript:alert(1)')).toBeNull()
    expect(safeExternalUrl('data:text/html,<script>alert(1)</script>')).toBeNull()
    expect(safeExternalUrl('file:///etc/passwd')).toBeNull()
    expect(safeExternalUrl('vbscript:msgbox(1)')).toBeNull()
  })

  it('rejects embedded credentials and control characters', () => {
    expect(safeExternalUrl('https://user:pass@example.com')).toBeNull()
    expect(safeExternalUrl('https://example.com/\r\nSet-Cookie: x=y')).toBeNull()
    expect(safeExternalUrl('https://example.com/\u0000')).toBeNull()
  })

  it('rejects empty and malformed values', () => {
    expect(safeExternalUrl('')).toBeNull()
    expect(safeExternalUrl('   ')).toBeNull()
    expect(safeExternalUrl(null)).toBeNull()
    expect(safeExternalUrl(undefined)).toBeNull()
    expect(safeExternalUrl('not a url')).toBeNull()
  })
})
