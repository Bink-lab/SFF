import os

path = r'C:\Users\sidney\AppData\Roaming\Python\Python314\site-packages\win10toast\__init__.py'

with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('from pkg_resources import Requirement', '# from pkg_resources import Requirement')
content = content.replace('from pkg_resources import resource_filename', '# from pkg_resources import resource_filename')
content = content.replace('icon_path = resource_filename(Requirement.parse("win10toast"), "win10toast/data/python.ico")', 'import os; icon_path = os.path.join(os.path.dirname(__file__), "data", "python.ico")')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

print("Patch applied")
