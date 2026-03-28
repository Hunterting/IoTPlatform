import re
import glob

files = glob.glob('Controllers/*.cs')
for filepath in files:
    with open(filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    modified = False
    for i in range(len(lines)):
        # 检查是否是需要修改的行
        line = lines[i]
        if re.search(r'return ApiResponse(<[^>]+>)?\.(BadRequest|Forbidden|Error|NotFound)\(', line):
            # 替换这行
            lines[i] = re.sub(r'return ApiResponse(<[^>]+>)?\.(BadRequest|Forbidden|Error|NotFound)\(',
                              r'return Ok(ApiResponse\1.\2(', lines[i])
            modified = True
            print(f'Modified {filepath} line {i+1}')
    
    if modified:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.writelines(lines)
        print(f'Saved {filepath}')
