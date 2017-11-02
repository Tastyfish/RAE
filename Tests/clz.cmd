@rae -s src/lazyquest.rad src/*.rad
@if errorlevel 1 goto error
@exit /b
:error
@echo Error occured.
@pause