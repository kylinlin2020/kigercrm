-- 5.9.24: align historical Lead phone lookup keys with the application normalizer.
-- Phone extensions are not part of the subscriber number. Reject values outside the
-- conservative 7..15 digit range instead of indexing fragments or concatenated extensions.
DO $$
BEGIN
    IF to_regclass('crm_lead') IS NOT NULL THEN
        UPDATE crm_lead
        SET phone_digits = CASE
            WHEN length(
                regexp_replace(
                    regexp_replace(
                        coalesce(phone, ''),
                        '(?i)(\mext(ension)?\.?[[:space:]]*|[;,#][[:space:]]*|[[:space:]]+x[[:space:]]*)[0-9]+[[:space:]]*$',
                        '',
                        'g'
                    ),
                    '[^0-9]',
                    '',
                    'g'
                )
            ) BETWEEN 7 AND 15
            THEN regexp_replace(
                regexp_replace(
                    coalesce(phone, ''),
                    '(?i)(\mext(ension)?\.?[[:space:]]*|[;,#][[:space:]]*|[[:space:]]+x[[:space:]]*)[0-9]+[[:space:]]*$',
                    '',
                    'g'
                ),
                '[^0-9]',
                '',
                'g'
            )
            ELSE NULL
        END;
    END IF;
END
$$;
