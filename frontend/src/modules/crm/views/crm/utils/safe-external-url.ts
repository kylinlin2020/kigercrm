/**
 * Normalize a user/provider-supplied URL before binding it to an anchor.
 * Only https/http are allowed; credentials and non-network schemes are rejected.
 * Returns null when the value is not a safe external URL.
 */
export function safeExternalUrl(raw: string | null | undefined): string | null {
  if (!raw || !raw.trim())
    return null
  const value = raw.trim()
  // Reject control characters (incl. embedded CRLF used to smuggle headers/schemes).
  if (Array.from(value).some(ch => ch.charCodeAt(0) < 0x20 || ch.charCodeAt(0) === 0x7F))
    return null
  try {
    const url = new URL(value)
    if (url.protocol !== 'https:' && url.protocol !== 'http:')
      return null
    if (url.username || url.password)
      return null
    return url.href
  }
  catch {
    return null
  }
}
