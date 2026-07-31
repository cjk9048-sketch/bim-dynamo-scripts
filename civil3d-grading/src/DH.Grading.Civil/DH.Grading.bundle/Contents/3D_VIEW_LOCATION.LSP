(defun c:v1()
(command "-view" "top")
(command "ucs" "")
)

(defun c:v2()
(command "-view" "_swiso")
)

(defun c:v3()
(command "-view" "_front")
)

(defun c:v4()
(command "-view" "_left")
)

(defun c:vv1()(command "_vscurrent" "_2d" ))

(defun c:vv2()(command "_vscurrent" "_r" ))

(defun c:vv3()(command "_vscurrent" "_g" ))





