"""Negative controls for VPS-10.7's production source and runtime boundary."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "verify-vps04b-runtime-boundary.py"
spec = importlib.util.spec_from_file_location("vps10_bridge_boundary", SCRIPT)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class BridgeBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.host = Path(self.temp.name) / "host"
        self.host.mkdir()
        self.project = self.host / gate.PROJECT.name
        self.project.write_text("<Project Sdk='Microsoft.NET.Sdk.Web' />")
        self.source = self.host / "Controller.cs"
        self.source.write_text("namespace Native; public sealed class Controller {}")

    def test_clean_source(self):
        gate.validate_source(self.host)

    def test_source_link(self):
        self.project.write_text("<Project><ItemGroup><Compile Include='../Legacy/*.cs' /></ItemGroup></Project>")
        with self.assertRaises(AssertionError):
            gate.validate_source(self.host)

    def test_fake_namespaces_and_dom_and_dispatch(self):
        for code in ("namespace System.Web.Http;", "namespace Microsoft.Owin;",
                     "class WireObject {}", "Action.Invoke(controller, args);",
                     "var value = request.GetOwinContext();"):
            with self.subTest(code=code):
                self.source.write_text(code)
                with self.assertRaises(AssertionError):
                    gate.validate_source(self.host)

    def test_retired_file(self):
        (self.host / "CompatWebApi.cs").write_text("namespace Native;")
        with self.assertRaises(AssertionError):
            gate.validate_source(self.host)

    def test_evaluated_inputs(self):
        gate.validate_compile_items(self.host, {"Items": {"Compile": [{"FullPath": str(self.source)}]}})
        for item in ({"FullPath": str(self.host.parent / "Legacy.cs")},
                     {"FullPath": str(self.source), "Link": "Legacy.cs"}):
            with self.subTest(item=item), self.assertRaises(AssertionError):
                gate.validate_compile_items(self.host, {"Items": {"Compile": [item]}})

    def test_runtime_target_only_dependency(self):
        clean = {"libraries": {"Native/1.0": {}}, "targets": {"net10": {"Native/1.0": {}}}}
        self.assertEqual(1, gate.validate_runtime(clean))
        for blocked in ("Newtonsoft.Json/13.0", "Microsoft.Owin/4.0", "System.Security.AccessControl/6.0"):
            with self.subTest(blocked=blocked), self.assertRaises(AssertionError):
                gate.validate_runtime({"libraries": clean["libraries"], "targets": {"net10": {blocked: {}}}})


if __name__ == "__main__":
    unittest.main()
