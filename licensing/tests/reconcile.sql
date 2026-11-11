SELECT
    (SELECT COUNT(*) FROM licenses) AS licenses,
    (SELECT COUNT(*) FROM devices) AS devices,
    (SELECT COUNT(*) FROM devices d LEFT JOIN licenses l ON l.id = d.license_id WHERE l.id IS NULL) AS orphaned_devices,
    (SELECT COUNT(*) FROM (SELECT code_hash FROM licenses GROUP BY code_hash HAVING COUNT(*) > 1)) AS duplicate_codes,
    (SELECT COUNT(*) FROM (SELECT token_hash FROM devices GROUP BY token_hash HAVING COUNT(*) > 1)) AS duplicate_legacy_tokens;
