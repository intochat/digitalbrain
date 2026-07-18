import os
import re

# Root directory of the repository
ROOT_DIR = r"e:\digitalbrain"

# Folders to ignore entirely
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

# Binary/unwanted extensions to ignore
IGNORE_EXTS = {
    ".png", ".jpg", ".jpeg", ".gif", ".ico", ".dll", ".exe", ".pdb",
    ".zip", ".tar", ".gz", ".rar", ".7z", ".mp3", ".mp4", ".wav",
    ".avi", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
    ".jar", ".class", ".so", ".dylib", ".a", ".lib", ".bin", ".suo",
    ".user", ".userprefs", ".sln.docstates"
}

def replace_case_insensitive(text):
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

def process_files():
    modified_count = 0
    for root, dirs, files in os.walk(ROOT_DIR):
        # Prune ignored directories in place
        dirs[:] = [d for d in dirs if d not in IGNORE_DIRS]
        
        for file in files:
            ext = os.path.splitext(file)[1].lower()
            if ext in IGNORE_EXTS:
                continue
            
            file_path = os.path.join(root, file)
            
            # Read content
            try:
                with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
                    content = f.read()
            except Exception as e:
                print(f"Skipping {file_path} due to read error: {e}")
                continue
            
            # Search for 'brainos' case-insensitively
            if re.search(r'brainos', content, flags=re.IGNORECASE):
                new_content = replace_case_insensitive(content)
                try:
                    with open(file_path, "w", encoding="utf-8") as f:
                        f.write(new_content)
                    print(f"Modified: {file_path}")
                    modified_count += 1
                except Exception as e:
                    print(f"Failed to write {file_path}: {e}")
                    
    print(f"Total files modified: {modified_count}")

if __name__ == "__main__":
    process_files()
