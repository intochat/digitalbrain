import os
import re

ROOT_DIR = r"e:\digitalbrain"

IGNORE_DIRS = {
    ".git",
    ".agents",
    "bin",
    "obj",
    ".vscode",
    ".claude",
    ".codegraph",
    "node_modules",
    "build",
    ".dart_tool",
    ".idea"
}

IGNORE_EXTS = {
    ".png", ".jpg", ".jpeg", ".gif", ".ico", ".dll", ".exe", ".pdb",
    ".zip", ".tar", ".gz", ".rar", ".7z", ".mp3", ".mp4", ".wav",
    ".avi", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
    ".jar", ".class", ".so", ".dylib", ".a", ".lib", ".bin", ".suo",
    ".user", ".userprefs", ".sln.docstates"
}

def rename_string(text):
    def repl(match):
        val = match.group(0)
        if val == 'BRAINOS':
            return 'DIGITALBRAIN'
        elif val == 'BrainOS':
            return 'DigitalBrain'
        elif val == 'Brainos':
            return 'DigitalBrain'
        elif val == 'brainos':
            return 'digitalbrain'
        else:
            if val[0].isupper():
                return 'DigitalBrain'
            else:
                return 'digitalbrain'
    return re.sub(r'brainos', repl, text, flags=re.IGNORECASE)

def process_file_contents():
    print("--- Starting File Contents Replacement ---")
    modified_count = 0
    for root, dirs, files in os.walk(ROOT_DIR):
        dirs[:] = [d for d in dirs if d not in IGNORE_DIRS]
        
        for file in files:
            ext = os.path.splitext(file)[1].lower()
            if ext in IGNORE_EXTS:
                continue
            
            file_path = os.path.join(root, file)
            try:
                with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
                    content = f.read()
            except Exception as e:
                print(f"Skipping read for {file_path}: {e}")
                continue
            
            if re.search(r'brainos', content, flags=re.IGNORECASE):
                new_content = rename_string(content)
                try:
                    with open(file_path, "w", encoding="utf-8") as f:
                        f.write(new_content)
                    print(f"Modified content in: {file_path}")
                    modified_count += 1
                except Exception as e:
                    print(f"Failed to write {file_path}: {e}")
    print(f"Total files with content modified: {modified_count}")

def rename_files_and_folders():
    print("--- Starting Files and Folders Renaming ---")
    renamed_files_count = 0
    renamed_dirs_count = 0
    
    # We walk bottom-up (topdown=False) to ensure children are renamed before parents
    for root, dirs, files in os.walk(ROOT_DIR, topdown=False):
        # Check if current path contains ignored directories
        parts = os.path.normpath(root).split(os.sep)
        if any(ignored in parts for ignored in IGNORE_DIRS):
            continue
            
        # First rename files in the current root
        for file in files:
            if re.search(r'brainos', file, flags=re.IGNORECASE):
                old_file_path = os.path.join(root, file)
                new_file_name = rename_string(file)
                new_file_path = os.path.join(root, new_file_name)
                try:
                    os.rename(old_file_path, new_file_path)
                    print(f"Renamed file: {old_file_path} -> {new_file_path}")
                    renamed_files_count += 1
                except Exception as e:
                    print(f"Failed to rename file {old_file_path}: {e}")
                    
        # Then rename subdirectories of the current root
        for d in dirs:
            if re.search(r'brainos', d, flags=re.IGNORECASE):
                old_dir_path = os.path.join(root, d)
                new_dir_name = rename_string(d)
                new_dir_path = os.path.join(root, new_dir_name)
                try:
                    os.rename(old_dir_path, new_dir_path)
                    print(f"Renamed directory: {old_dir_path} -> {new_dir_path}")
                    renamed_dirs_count += 1
                except Exception as e:
                    print(f"Failed to rename directory {old_dir_path}: {e}")
                    
    print(f"Total files renamed: {renamed_files_count}")
    print(f"Total directories renamed: {renamed_dirs_count}")

if __name__ == "__main__":
    process_file_contents()
    rename_files_and_folders()
