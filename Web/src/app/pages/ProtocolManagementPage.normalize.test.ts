/**
 * Permanent regression test for `normalizeLegacyConfigKeys`.
 *
 * Mirrors the backend `ProtocolConfigNormalizer` behavior contract
 * (Data/ProtocolConfigNormalizer.cs + tests/IoTPlatform.AnSheng.Tests/
 * ProtocolConfigNormalizerTests.cs) so the three stacks (backend / frontend /
 * SQL) stay in lock-step. If anyone later changes the parse branch or the rule
 * numbering, this fails loudly instead of shipping a silent value drift.
 *
 * Test names intentionally align with the backend suite for cross-locating:
 *   JsonNull_BehaviorMatrix
 *   EmptyString_OnStringProperty_IsPreserved
 *   NumericProperty_IntegerParsingBoundary_IsLockedForCrossStackParity
 */
import { describe, it, expect } from 'vitest';
import { normalizeLegacyConfigKeys } from './ProtocolManagementPage';

describe('normalizeLegacyConfigKeys — cross-stack parity', () => {
  // ── Backend: JsonNull_BehaviorMatrix ───────────────────────────────────────
  describe('JsonNull_BehaviorMatrix (rule 0: null deletes the key, all props)', () => {
    it('host/y + Host/x → {Host:"x"} (canonical wins, order-independent)', () => {
      expect(normalizeLegacyConfigKeys('mqtt', { host: 'y', Host: 'x' })).toEqual({ Host: 'x' });
      expect(normalizeLegacyConfigKeys('mqtt', { Host: 'x', host: 'y' })).toEqual({ Host: 'x' });
    });

    it('host/x + Host/null → {Host:"x"} (rescue: null canonical key does not evict the real lowercase value)', () => {
      expect(normalizeLegacyConfigKeys('mqtt', { host: 'x', Host: null })).toEqual({ Host: 'x' });
    });

    it('Host/null → {} (rule 0 deletes the null key)', () => {
      expect(normalizeLegacyConfigKeys('mqtt', { Host: null })).toEqual({});
    });

    it('port/"502" + Port/null → {Port:502} (rescue + numeric coercion)', () => {
      expect(normalizeLegacyConfigKeys('modbus', { port: '502', Port: null })).toEqual({ Port: 502 });
    });

    it('opcua CertificatePath/null → {} (canonical nullable key deleted)', () => {
      expect(normalizeLegacyConfigKeys('opcua', { CertificatePath: null })).toEqual({});
    });

    it('mqtt CertificatePath/null → {} (rule 0 ALSO removes UNKNOWN null keys)', () => {
      // Tricky point: rule 0 is not limited to canonical keys.
      expect(normalizeLegacyConfigKeys('mqtt', { CertificatePath: null })).toEqual({});
    });
  });

  // ── Backend: EmptyString_OnStringProperty_IsPreserved ──────────────────────
  describe('EmptyString_OnStringProperty_IsPreserved (empty string drops ONLY for numeric props)', () => {
    it('Host:"" is PRESERVED (empty string is a legal value for a string property)', () => {
      expect(normalizeLegacyConfigKeys('mqtt', { Host: '' })).toEqual({ Host: '' });
    });

    it('Port:"" is DELETED (empty string on a numeric property would throw on bind)', () => {
      expect(normalizeLegacyConfigKeys('mqtt', { Port: '' })).toEqual({});
    });
  });

  // ── Backend: NumericProperty_IntegerParsingBoundary_IsLockedForCrossStackParity ──
  describe('NumericProperty_IntegerParsingBoundary_IsLockedForCrossStackParity', () => {
    // modbus Port is numeric. Each input must map to exactly what C# long.TryParse
    // (NumberStyles.Integer = AllowLeadingWhite|AllowTrailingWhite|AllowLeadingSign)
    // produces. The leading '+' is the 4th cross-stack divergence point.
    it('"502" → 502', () => {
      expect(normalizeLegacyConfigKeys('modbus', { port: '502' })).toEqual({ Port: 502 });
    });
    it('"  502  " → 502 (Trim)', () => {
      expect(normalizeLegacyConfigKeys('modbus', { port: '  502  ' })).toEqual({ Port: 502 });
    });
    it('"+502" → 502 (leading + allowed by AllowLeadingSign)', () => {
      expect(normalizeLegacyConfigKeys('modbus', { port: '+502' })).toEqual({ Port: 502 });
    });
    it('"-1" → -1', () => {
      expect(normalizeLegacyConfigKeys('modbus', { port: '-1' })).toEqual({ Port: -1 });
    });
    it('"0502" → 502 (leading zeros allowed)', () => {
      expect(normalizeLegacyConfigKeys('modbus', { port: '0502' })).toEqual({ Port: 502 });
    });
    it('"50x2" → kept as string (no guessing, unlike parseInt)', () => {
      expect(normalizeLegacyConfigKeys('modbus', { port: '50x2' })).toEqual({ Port: '50x2' });
    });
    it('"5000.5" → kept as string (no guessing, unlike Number)', () => {
      expect(normalizeLegacyConfigKeys('modbus', { port: '5000.5' })).toEqual({ Port: '5000.5' });
    });
    it('"1e3" → kept as string (scientific notation not recognized, unlike Number)', () => {
      expect(normalizeLegacyConfigKeys('modbus', { port: '1e3' })).toEqual({ Port: '1e3' });
    });
  });

  // ── First-occurrence-wins (deliberately OPPOSITE to STJ's last-wins) ────────
  describe('duplicate old keys hitting the same target → first-occurrence wins', () => {
    it('serialPort + serialport → PortName:"COM3" (first wins, not STJ last-wins 5502)', () => {
      expect(
        normalizeLegacyConfigKeys('modbusrtu', { serialPort: 'COM3', serialport: 'COM1' })
      ).toEqual({ PortName: 'COM3' });
    });
  });

  // ── Numeric property set must be exactly the 9 backend props ────────────────
  describe('numeric property set parity (9 props)', () => {
    it('Port / BaudRate coerce; Host / EndpointUrl do NOT', () => {
      expect(normalizeLegacyConfigKeys('modbus', { port: '502' })).toEqual({ Port: 502 });
      expect(normalizeLegacyConfigKeys('modbusrtu', { baudrate: '9600' })).toEqual({ BaudRate: 9600 });
      // string property with integer-looking value must stay a string
      expect(normalizeLegacyConfigKeys('mqtt', { Host: '502' })).toEqual({ Host: '502' });
    });
  });

  // ── Unknown protocol type → passthrough (smallest blast radius) ────────────
  describe('unknown protocol type → passthrough unchanged', () => {
    it('bacnet (unrecognized) returns the config as-is', () => {
      expect(normalizeLegacyConfigKeys('bacnet', { host: 'x', Port: '502' })).toEqual({
        host: 'x',
        Port: '502',
      });
    });
  });

  // ── Modbus RTU field name must be PortName (not SerialPort) ────────────────
  describe('Modbus RTU uses PortName (not SerialPort)', () => {
    it('serialport alias → PortName; baudrate coerced', () => {
      expect(
        normalizeLegacyConfigKeys('modbusrtu', { serialport: 'COM1', baudrate: '9600' })
      ).toEqual({ PortName: 'COM1', BaudRate: 9600 });
    });
  });
});
