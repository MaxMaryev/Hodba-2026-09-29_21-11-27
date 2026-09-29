from pathlib import Path
import unittest, json
ROOT=Path(__file__).resolve().parents[2]
class TravelerValidation(unittest.TestCase):
 def test_deliverables_and_geometry(self):
  for p in ['Assets/Art/Field/Models/M3_Traveler.fbx','ArtSource/Field/M3_Traveler.blend','ArtSource/Field/traveler_validation.json']:
   self.assertTrue((ROOT/p).is_file(), p)
  r=json.loads((ROOT/'ArtSource/Field/traveler_validation.json').read_text())
  self.assertTrue(5000 <= r['triangles'] <= 8000)
  self.assertAlmostEqual(r['height_m'],1.75,places=2)
  self.assertEqual(r['unweighted_vertices'],0)
  self.assertTrue(r['loop_endpoints_identical'])
  self.assertEqual(r['animation_names'],['Idle_Breathing','Walk_Tired'])
  self.assertTrue(r['fbx_roundtrip_valid'])
if __name__=='__main__': unittest.main()
