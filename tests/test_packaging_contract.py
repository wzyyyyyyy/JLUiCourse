from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]


class PackagingContractTests(unittest.TestCase):
    def test_packaging_scripts_exist_for_three_platform_installers(self):
        expected_files = [
            ROOT / "scripts" / "package" / "windows.ps1",
            ROOT / "scripts" / "package" / "linux.sh",
            ROOT / "scripts" / "package" / "macos.sh",
            ROOT / ".github" / "workflows" / "package.yml",
        ]

        missing = [str(path.relative_to(ROOT)) for path in expected_files if not path.exists()]

        self.assertEqual([], missing)

    def test_package_workflow_builds_native_installers_and_keeps_macos_unsigned(self):
        workflow = (ROOT / ".github" / "workflows" / "package.yml").read_text(encoding="utf-8")

        required_snippets = [
            "windows-latest",
            "ubuntu-latest",
            "macos-latest",
            "scripts/package/windows.ps1",
            "scripts/package/linux.sh",
            "scripts/package/macos.sh",
            "iCourse-windows-x64-installer",
            "iCourse-linux-x64-installer",
            "iCourse-macos-universal-unsigned",
        ]

        missing = [snippet for snippet in required_snippets if snippet not in workflow]

        self.assertEqual([], missing)

    def test_macos_script_documents_unsigned_gatekeeper_behavior(self):
        script = (ROOT / "scripts" / "package" / "macos.sh").read_text(encoding="utf-8")

        self.assertIn("unsigned", script)
        self.assertIn("Gatekeeper", script)
        self.assertIn("notarization", script)
        self.assertIn("hdiutil create", script)

    def test_windows_installer_uses_bundled_inno_language_files(self):
        script = (ROOT / "scripts" / "package" / "windows.ps1").read_text(encoding="utf-8")

        self.assertIn('MessagesFile: "compiler:Default.isl"', script)
        self.assertNotIn("ChineseSimplified.isl", script)

    def test_projects_and_ci_target_dotnet_10(self):
        project_files = [
            ROOT / "iCourse" / "iCourse.csproj",
            ROOT / "iCourse.Tests" / "iCourse.Tests.csproj",
        ]
        workflow_files = [
            ROOT / ".github" / "workflows" / "dotnet.yml",
            ROOT / ".github" / "workflows" / "package.yml",
        ]

        for project_file in project_files:
            with self.subTest(project=str(project_file.relative_to(ROOT))):
                self.assertIn("<TargetFramework>net10.0</TargetFramework>", project_file.read_text(encoding="utf-8"))

        for workflow_file in workflow_files:
            with self.subTest(workflow=str(workflow_file.relative_to(ROOT))):
                self.assertIn('dotnet-version: "10.0.x"', workflow_file.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
