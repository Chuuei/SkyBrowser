import math
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from TargetDistanceSources import (PARSEC_LIGHT_YEARS, angular_separation, eligible_measurement,
                                   identifier, measurement_rank, parse_nasa_fact, votable_rows)


class DistanceSourceTests(unittest.TestCase):
    def row(self, **changes):
        return dict(designation="NGC 123", dist=100.0, unit="pc", qual="", method="paral",
                    bibcode="2021MNRAS.504..356D", otype="OpC", minus_err=-2.0, plus_err=3.0) | changes

    def test_identity_normalisation_rejects_component_and_member_names(self):
        self.assertEqual("NGC123", identifier("NGC  0123"))
        self.assertEqual("IC11", identifier("IC 0011"))
        self.assertEqual("NGC123A", identifier("NGC 0123A"))
        for member in ["NGC 123 5", "NGC123 NED01", "IC 11 IRS 1", "NGC123/124"]:
            self.assertIsNone(identifier(member))

    def test_units_asymmetric_errors_and_null_values(self):
        for unit, factor in [("pc", 1), ("kpc", 1000), ("Mpc", 1000000)]:
            distance, errors = eligible_measurement(self.row(unit=unit))
            self.assertAlmostEqual(distance, 100 * factor * PARSEC_LIGHT_YEARS)
            self.assertEqual(errors, [2 * factor * PARSEC_LIGHT_YEARS, 3 * factor * PARSEC_LIGHT_YEARS])
        self.assertEqual([None, None], eligible_measurement(self.row(minus_err=math.nan, plus_err=0))[1])

    def test_invalid_values_uncertain_flags_and_redshift_flow_are_not_distances(self):
        for change in [dict(dist=0), dict(dist=-1), dict(dist=math.inf), dict(dist=math.nan),
                       dict(unit="au"), dict(qual=":"), dict(minus_err=-100), dict(plus_err=100),
                       dict(bibcode="unreferenced"), dict(method="redshift"), dict(method="Redshift"),
                       dict(method="kin"), dict(method="Flow"), dict(method="?")]:
            self.assertIsNone(eligible_measurement(self.row(**change)), change)
        self.assertIsNone(eligible_measurement(self.row(method="", otype="G")))
        self.assertIsNotNone(eligible_measurement(self.row(method="", otype="GlC")))
        self.assertIsNone(eligible_measurement(self.row(method="", dist=200000)))

    def test_selection_prefers_newest_paper_then_reported_relative_error(self):
        old = self.row(bibcode="2020MNRAS.504..356D", minus_err=-1, plus_err=1)
        new = self.row(minus_err=-4, plus_err=4)
        better = self.row(minus_err=-3, plus_err=3)
        chosen = min([(row, eligible_measurement(row)) for row in [old, new, better]], key=measurement_rank)
        self.assertEqual(better, chosen[0])

    def test_truncated_tap_results_are_rejected(self):
        for status in ["OVERFLOW", "ERROR"]:
            data = f'<VOTABLE><RESOURCE><INFO name="QUERY_STATUS" value="OK"/><TABLE><DATA><TABLEDATA/></DATA></TABLE><INFO name="QUERY_STATUS" value="{status}"/></RESOURCE></VOTABLE>'.encode()
            with self.assertRaises(ValueError):
                votable_rows(data)

    def test_nasa_ranges_limits_and_multiple_values_are_not_single_estimates(self):
        for value in ["1,000–2,000 light-years", "at least 1,000 light-years", "1,000 or 2,000 light-years"]:
            self.assertIsNone(parse_nasa_fact(f">Distance</h2><p>{value}</p>"))
        self.assertEqual((3000000, "About 3 million light-years"), parse_nasa_fact(">Distance</h2><p>About 3 million light-years</p>"))

    def test_coordinate_checks_handle_the_ra_seam(self):
        self.assertAlmostEqual(0.02, angular_separation((359.99, 0), (0.01, 0)))
        self.assertAlmostEqual(2, angular_separation((0, 89), (180, 89)))


if __name__ == "__main__":
    unittest.main()
