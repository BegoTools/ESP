from access_probe import shell,privileged
result=shell(privileged('setprop service.adb.root 0; getprop service.adb.root'))
print(result)
assert result['exit_code']==0 and result['stdout']=='0'
